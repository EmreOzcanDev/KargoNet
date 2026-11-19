namespace KargoNet.Messaging.Consuming;

/// <summary>
/// Thin seam over the broker calls a consumer makes, so the delivery/retry/DLQ
/// pipeline is unit-testable with a fake and no live broker.
/// </summary>
public interface IConsumerBroker
{
    Task AckAsync(ulong deliveryTag);

    Task PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers);
}
