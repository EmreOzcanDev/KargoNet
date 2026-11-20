using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace KargoNet.Messaging.Infra;

/// <summary>
/// Opens its RabbitMQ connection and channel on FIRST use and caches them
/// afterwards (same lazy pattern as
/// <c>KargoNet.Messaging.Publishing.LazyOutboxChannelFactory</c>). Nothing
/// connects at startup, so hosts start and serve traffic without a reachable
/// broker; a missing broker surfaces on the first channel call.
/// </summary>
public class LazyChannelFactory
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private IChannel? _channel;

    public LazyChannelFactory(IConfiguration configuration)
        => _configuration = configuration;

    public virtual async Task<IChannel> GetChannelAsync()
    {
        // Guard the check-then-act across the await: concurrent first calls
        // must open exactly one connection/channel (the loser re-checks
        // _channel under the lock instead of leaking its own).
        if (_channel is null)
        {
            await _openLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _channel ??= await OpenChannelAsync().ConfigureAwait(false);
            }
            finally
            {
                _openLock.Release();
            }
        }

        return _channel;
    }

    private async Task<IChannel> OpenChannelAsync()
    {
        var connectionString = _configuration.GetConnectionString("RabbitMq") ?? "amqp://localhost";
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

        var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
        return await connection.CreateChannelAsync(
            new CreateChannelOptions(false, false)).ConfigureAwait(false);
    }
}
