using KargoNet.Contracts.Events;
using KargoNet.Messaging.Topology;

namespace KargoNet.Messaging.Consuming;

/// <summary>
/// Delivery pipeline for event consumers: deserialize → handle → ack on success;
/// on failure decide via <see cref="RetryPolicy"/> between republishing to the
/// retry queue (with <c>x-retry-count</c> and <c>x-exception</c> headers) and
/// routing to the DLQ. The delivery is always acked — at-least-once delivery
/// comes from the retry TTL + dead-letter arguments, never from nacks.
/// </summary>
public abstract class ConsumerBase<TPayload>
    where TPayload : class
{
    public const string RetryCountHeader = "x-retry-count";
    public const string ExceptionHeader = "x-exception";

    private readonly IConsumerBroker _broker;
    private readonly Func<EventEnvelope<TPayload>, Task> _handler;

    protected ConsumerBase(IConsumerBroker broker, Func<EventEnvelope<TPayload>, Task> handler)
    {
        _broker = broker;
        _handler = handler;
    }

    public async Task HandleDeliveryAsync(ulong deliveryTag, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
    {
        var envelope = EventSerializer.Deserialize<TPayload>(body.Span);

        try
        {
            await _handler(envelope).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var retryCount = headers is not null && headers.TryGetValue(RetryCountHeader, out var raw)
                ? Convert.ToInt32(raw)
                : 0;

            var newHeaders = headers is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(headers);
            newHeaders[ExceptionHeader] = exception.Message;

            var decision = RetryPolicy.Decide(retryCount, exception);
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

            await _broker.PublishAsync(RabbitTopology.Exchange, routingKey, body, newHeaders).ConfigureAwait(false);
        }

        // Always ack: redelivery is owned by the retry TTL + dead-lettering, not by nacks.
        await _broker.AckAsync(deliveryTag).ConfigureAwait(false);
    }
}
