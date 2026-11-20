using System.Text.Json;
using System.Text.Json.Serialization;
using KargoNet.Contracts.Events;
using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Topology;
using Microsoft.Extensions.Logging;

namespace KargoNet.Tracking.Api.Consuming;

/// <summary>
/// Delivery pipeline for the tracking read model (plain class in this plan;
/// it becomes the hosted consumer in plan 04-03). Mirrors
/// <see cref="ConsumerBase{T}"/> exactly: deserialize → dispatch on
/// <c>EventType</c> → ack on success; on failure decide via
/// <see cref="RetryPolicy"/> between republish (with <c>x-retry-count</c> and
/// <c>x-exception</c>) and DLQ routing. The delivery is ALWAYS acked —
/// at-least-once comes from the retry TTL + dead-letter arguments, never
/// from nacks.
/// </summary>
public sealed class ShipmentEventsConsumer
{
    public const string CreatedEventType = "shipment.created";
    public const string StatusChangedEventType = "shipment.status.changed";

    private const string RetryCountHeader = "x-retry-count";
    private const string ExceptionHeader = "x-exception";

    private readonly IConsumerBroker _broker;
    private readonly ShipmentEventHandler _handler;
    private readonly ILogger<ShipmentEventsConsumer> _logger;

    /// <summary>
    /// Must match <see cref="EventSerializer"/>'s wire format (camelCase,
    /// enum names) — payloads are published by the Shipment service through
    /// that serializer and must decode identically here.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = CreatePayloadOptions();

    public ShipmentEventsConsumer(
        IConsumerBroker broker,
        ShipmentEventHandler handler,
        ILogger<ShipmentEventsConsumer> logger)
    {
        _broker = broker;
        _handler = handler;
        _logger = logger;
    }

    public async Task HandleDeliveryAsync(ulong deliveryTag, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
    {
        EventEnvelope<JsonElement?>? envelope = null;
        try
        {
            // Decode inside the failure path: a malformed body must take the
            // same retry/DLQ routing as a handler failure, never escape.
            envelope = EventSerializer.Deserialize<JsonElement?>(body.Span);

            await HandleAsync(envelope).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failure handling delivery {DeliveryTag}; routing per RetryPolicy.", deliveryTag);

            var retryCount = headers is not null && headers.TryGetValue(RetryCountHeader, out var raw)
                ? ParseRetryCount(raw)
                : 0;

            var newHeaders = headers is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(headers);
            newHeaders[ExceptionHeader] = exception.Message;

            var decision = envelope is null
                ? new RetryDecision.RouteToDlq()
                : RetryPolicy.Decide(retryCount, exception);
            string routingKey;

            switch (decision)
            {
                case RetryDecision.RepublishToRetry republish:
                    newHeaders[RetryCountHeader] = republish.NewRetryCount;
                    routingKey = RabbitTopology.RetryRoutingKey;
                    break;
                case RetryDecision.RouteToDlq:
                    routingKey = RabbitTopology.DlqRoutingKey;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected retry decision: {decision.GetType().Name}");
            }

            // The republish is best-effort: if it fails, the original delivery
            // is still acked below (the "every failure path ends in ack"
            // invariant). No requeue.
            try
            {
                await _broker.PublishAsync(RabbitTopology.Exchange, routingKey, body, newHeaders).ConfigureAwait(false);
            }
            catch
            {
                // Deliberately swallowed; the ack below must run on every path.
            }
        }

        // Always ack: redelivery is owned by the retry TTL + dead-lettering, not by nacks.
        await _broker.AckAsync(deliveryTag).ConfigureAwait(false);
    }

    private Task HandleAsync(EventEnvelope<JsonElement?> envelope)
    {
        switch (envelope.EventType)
        {
            case CreatedEventType:
                var created = envelope.Payload!.Value.Deserialize<ShipmentCreatedPayload>(PayloadOptions)!;
                return _handler.ApplyCreatedAsync(
                    envelope.EventId,
                    envelope.EventType,
                    envelope.OccurredAt,
                    envelope.Payload!.Value.GetRawText(),
                    created);

            case StatusChangedEventType:
                var changed = envelope.Payload!.Value.Deserialize<ShipmentStatusChangedPayload>(PayloadOptions)!;
                return _handler.ApplyStatusChangedAsync(
                    envelope.EventId,
                    envelope.EventType,
                    envelope.OccurredAt,
                    envelope.Payload!.Value.GetRawText(),
                    changed);

            default:
                throw new NotSupportedException($"Unknown event type: {envelope.EventType}");
        }
    }

    /// <summary>
    /// Defensively parses the untrusted <c>x-retry-count</c> header (mirrors
    /// <see cref="ConsumerBase{T}"/>): accepts <see cref="int"/> and in-range
    /// <see cref="long"/> values; anything else is the first attempt (0).
    /// </summary>
    private static int ParseRetryCount(object? raw)
        => raw switch
        {
            int count => count,
            long count when count is >= int.MinValue and <= int.MaxValue => (int)count,
            _ => 0,
        };

    private static JsonSerializerOptions CreatePayloadOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
