using KargoNet.Messaging.Infra;
using KargoNet.Messaging.Topology;
using RabbitMQ.Client;

namespace KargoNet.Tracking.Api.Dlq;

/// <summary>
/// <see cref="IDlqAdmin"/> on RabbitMQ. <see cref="CountAsync"/> uses a
/// passive queue declare (no side effects); <see cref="ReplayAsync"/> drains
/// the DLQ with <c>BasicGet</c> and republishes each body to
/// <see cref="RabbitTopology.Exchange"/> under
/// <see cref="RabbitTopology.ShipmentRoutingKey"/> with NO
/// <c>x-retry-count</c> header, so replayed messages start a fresh retry
/// budget.
/// </summary>
public sealed class RabbitDlqAdmin : IDlqAdmin
{
    private readonly LazyChannelFactory _channelFactory;
    private readonly ILogger<RabbitDlqAdmin> _logger;

    public RabbitDlqAdmin(LazyChannelFactory channelFactory, ILogger<RabbitDlqAdmin> logger)
    {
        _channelFactory = channelFactory;
        _logger = logger;
    }

    public async Task<int> CountAsync()
    {
        var channel = await _channelFactory.GetChannelAsync().ConfigureAwait(false);
        var passive = await channel.QueueDeclarePassiveAsync(RabbitTopology.Dlq).ConfigureAwait(false);
        return (int)passive.MessageCount;
    }

    public async Task<int> ReplayAsync()
    {
        var channel = await _channelFactory.GetChannelAsync().ConfigureAwait(false);
        var moved = 0;

        while (true)
        {
            // BasicGet with auto-ack: the message leaves the DLQ the moment it
            // is read, so a crash mid-replay loses at most the in-flight body.
            var delivery = await channel.BasicGetAsync(RabbitTopology.Dlq, autoAck: true).ConfigureAwait(false);
            if (delivery is null)
                break;

            // No DeliveryProperties: a fresh delivery with no x-retry-count.
            await channel.BasicPublishAsync(
                RabbitTopology.Exchange,
                RabbitTopology.ShipmentRoutingKey,
                delivery.Body).ConfigureAwait(false);
            moved++;
        }

        _logger.LogInformation(
            "Replayed {Moved} message(s) from {Dlq} to {Exchange} (routing key {RoutingKey}).",
            moved, RabbitTopology.Dlq, RabbitTopology.Exchange, RabbitTopology.ShipmentRoutingKey);

        return moved;
    }
}
