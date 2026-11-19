namespace KargoNet.Messaging.Publishing;

/// <summary>
/// Thin seam isolating the single broker publish call, so the publisher's
/// confirm handling is unit-testable with no live broker.
/// </summary>
public interface IOutboxChannel
{
    Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers);
}
