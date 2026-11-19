using KargoNet.Messaging.Topology;

namespace KargoNet.Messaging.Publishing;

/// <summary>
/// Publishes shipment events to the fixed topic exchange <see cref="RabbitTopology.Exchange"/>
/// with publisher confirms: the broker confirm is awaited and an unconfirmed publish
/// is surfaced to the caller as <see cref="PublishNotConfirmedException"/>.
/// </summary>
public sealed class MessagePublisher
{
    private readonly IOutboxChannel _channel;

    public MessagePublisher(IOutboxChannel channel)
        => _channel = channel;

    public async Task PublishAsync(string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
    {
        var outcome = await _channel.PublishAsync(RabbitTopology.Exchange, routingKey, body, headers).ConfigureAwait(false);

        if (!outcome.IsConfirmed)
            throw new PublishNotConfirmedException(routingKey, outcome.PublishId);
    }
}

/// <summary>
/// Thrown when the broker does not confirm a publish, so a lost message is
/// surfaced to the caller instead of being silently dropped.
/// </summary>
public sealed class PublishNotConfirmedException : Exception
{
    public PublishNotConfirmedException(string routingKey, long publishId)
        : base($"Publish to exchange '{RabbitTopology.Exchange}' with routing key '{routingKey}' (publish id {publishId}) was not confirmed by the broker.")
    {
    }
}
