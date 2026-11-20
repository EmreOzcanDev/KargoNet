using System.Text.Json;
using System.Text.Json.Serialization;
using KargoNet.Contracts.Events;
using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Infra;
using KargoNet.Messaging.Topology;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KargoNet.Tracking.Api.Consuming;

/// <summary>
/// Hosted consumer feeding the tracking read model (plan 04-03).
/// <see cref="ExecuteAsync"/> opens a channel, declares the topology, and
/// subscribes <see cref="HandleDeliveryAsync"/> to
/// <see cref="RabbitTopology.MainQueue"/>; any connection or consume
/// failure is logged and retried on a 2-second loop — the host never dies
/// (design D1, threat T-04-08). The delivery pipeline mirrors
/// <see cref="ConsumerBase{T}"/> exactly: deserialize → dispatch on
/// <c>EventType</c> → ack on success; on failure decide via
/// <see cref="RetryPolicy"/> between republish (with <c>x-retry-count</c>
/// and <c>x-exception</c>) and DLQ routing. The delivery is ALWAYS acked —
/// at-least-once comes from the retry TTL + dead-letter arguments, never
/// from nacks.
/// </summary>
public sealed class ShipmentEventsConsumer : BackgroundService
{
    public const string CreatedEventType = "shipment.created";
    public const string StatusChangedEventType = "shipment.status.changed";

    private const string RetryCountHeader = "x-retry-count";
    private const string ExceptionHeader = "x-exception";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitConsumerBroker _broker;
    private readonly LazyChannelFactory _channelFactory;
    private readonly ILogger<ShipmentEventsConsumer> _logger;

    /// <summary>
    /// Must match <see cref="EventSerializer"/>'s wire format (camelCase,
    /// enum names) — payloads are published by the Shipment service through
    /// that serializer and must decode identically here.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = CreatePayloadOptions();

    public ShipmentEventsConsumer(
        IServiceScopeFactory scopeFactory,
        RabbitConsumerBroker broker,
        LazyChannelFactory channelFactory,
        ILogger<ShipmentEventsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _broker = broker;
        _channelFactory = channelFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var channel = await _channelFactory.GetChannelAsync().ConfigureAwait(false);
                await RabbitTopology.DeclareAsync(channel).ConfigureAwait(false);
                await _broker.ConsumeAsync(RabbitTopology.MainQueue, HandleDeliveryAsync, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A broker blip (no reachable broker is a Phase 6 deferral
                // condition here) must never kill the host: log, wait 2 s,
                // and try again (D1 / T-04-08).
                _logger.LogWarning(
                    exception,
                    "Consume loop failed; retrying in {RetryDelay}s.",
                    RetryDelay.TotalSeconds);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public async Task HandleDeliveryAsync(ulong deliveryTag, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
    {
        // The read-model context is DI-scoped: resolve the handler from a
        // fresh scope per delivery so concurrent deliveries never share a
        // <see cref="TrackingDbContext"/>.
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ShipmentEventHandler>();

        EventEnvelope<JsonElement?>? envelope = null;
        try
        {
            // Decode inside the failure path: a malformed body must take the
            // same retry/DLQ routing as a handler failure, never escape.
            envelope = EventSerializer.Deserialize<JsonElement?>(body.Span);

            await HandleAsync(handler, envelope).ConfigureAwait(false);
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

    private Task HandleAsync(ShipmentEventHandler handler, EventEnvelope<JsonElement?> envelope)
    {
        switch (envelope.EventType)
        {
            case CreatedEventType:
                var created = envelope.Payload!.Value.Deserialize<ShipmentCreatedPayload>(PayloadOptions)!;
                return handler.ApplyCreatedAsync(
                    envelope.EventId,
                    envelope.EventType,
                    envelope.OccurredAt,
                    envelope.Payload!.Value.GetRawText(),
                    created);

            case StatusChangedEventType:
                var changed = envelope.Payload!.Value.Deserialize<ShipmentStatusChangedPayload>(PayloadOptions)!;
                return handler.ApplyStatusChangedAsync(
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
