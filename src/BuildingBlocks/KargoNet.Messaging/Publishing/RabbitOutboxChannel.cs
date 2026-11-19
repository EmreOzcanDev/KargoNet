using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace KargoNet.Messaging.Publishing;

/// <summary>
/// Real <see cref="IOutboxChannel"/> adapter over RabbitMQ.Client's <see cref="IChannel"/>.
/// The 7.x client has no per-publish result: publisher confirms are
/// channel-scoped (enabled via <c>CreateChannelOptions</c> at channel setup) and
/// broker acks arrive on <see cref="IChannel.BasicAcksAsync"/>. We publish, then
/// await the ack for this publish's delivery tag, bounded by the channel's
/// continuation timeout. The live path is exercised in Phase 6 against a real broker.
/// </summary>
public sealed class RabbitOutboxChannel : IOutboxChannel
{
    private readonly IChannel _channel;

    public RabbitOutboxChannel(IChannel channel)
        => _channel = channel;

    public async Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
    {
        var properties = new BasicProperties();

        if (headers is { Count: > 0 })
        {
            var table = new Dictionary<string, object?>();
            foreach (var header in headers)
                table[header.Key] = header.Value;
            properties.Headers = table;
        }

        var publishId = (long)await _channel.GetNextPublishSequenceNumberAsync().ConfigureAwait(false);
        var acked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        AsyncEventHandler<BasicAckEventArgs> onAcks = (object sender, BasicAckEventArgs e) =>
        {
            // Multiple acks cover all tags up to e.DeliveryTag.
            if (e.Multiple || e.DeliveryTag >= (ulong)publishId)
                acked.TrySetResult(true);
            return Task.CompletedTask;
        };

        _channel.BasicAcksAsync += onAcks;
        try
        {
            await _channel.BasicPublishAsync(exchange, routingKey, mandatory: false, properties, body).ConfigureAwait(false);

            var completed = await Task.WhenAny(acked.Task, Task.Delay(_channel.ContinuationTimeout)).ConfigureAwait(false);

            return new PublishOutcome(completed == acked.Task, publishId);
        }
        finally
        {
            _channel.BasicAcksAsync -= onAcks;
        }
    }
}
