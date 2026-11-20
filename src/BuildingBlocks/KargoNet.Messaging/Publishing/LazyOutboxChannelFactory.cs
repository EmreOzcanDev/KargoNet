using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace KargoNet.Messaging.Publishing;

/// <summary>
/// <see cref="IOutboxChannel"/> that opens its RabbitMQ connection and channel
/// on FIRST publish and caches them afterwards. Nothing connects at startup, so
/// the host starts and serves traffic without a reachable broker; a missing
/// broker surfaces as a publish exception (which the outbox publisher turns
/// into leave-pending).
/// </summary>
public sealed class LazyOutboxChannelFactory : IOutboxChannel
{
    private readonly IConfiguration _configuration;
    private RabbitOutboxChannel? _channel;

    public LazyOutboxChannelFactory(IConfiguration configuration)
        => _configuration = configuration;

    public async Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
    {
        var channel = _channel ?? await OpenChannelAsync().ConfigureAwait(false);
        _channel = channel;
        return await channel.PublishAsync(exchange, routingKey, body, headers).ConfigureAwait(false);
    }

    private async Task<RabbitOutboxChannel> OpenChannelAsync()
    {
        var connectionString = _configuration.GetConnectionString("RabbitMq") ?? "amqp://localhost";
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

        var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
        var brokerChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, false)).ConfigureAwait(false);

        return new RabbitOutboxChannel(brokerChannel);
    }
}
