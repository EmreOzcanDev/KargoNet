using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace KargoNet.Messaging.Infra;

/// <summary>
/// Opens its RabbitMQ connection and channel on FIRST use and caches them
/// afterwards (same lazy pattern as
/// <c>KargoNet.Messaging.Publishing.LazyOutboxChannelFactory</c>). Nothing
/// connects at startup, so hosts start and serve traffic without a reachable
/// broker; a missing broker surfaces on the first channel call. A channel the
/// broker shuts down is replaced transparently on the next
/// <see cref="GetChannelAsync"/> call. <see cref="CreateChannelAsync"/> hands
/// out a fresh per-operation channel (the caller owns disposal) for work that
/// must not ride a long-lived shared channel.
/// </summary>
public class LazyChannelFactory
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private readonly object _gate = new();
    private IChannel? _channel;

    public LazyChannelFactory(IConfiguration configuration)
        => _configuration = configuration;

    /// <summary>
    /// Returns the cached channel, opening one on first use. If the broker
    /// has shut the cached channel down, a fresh channel is opened and
    /// cached before the call returns.
    /// </summary>
    public virtual async Task<IChannel> GetChannelAsync()
    {
        while (true)
        {
            await _openLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Fast path: an open cached channel, set by a previous call or,
                // while we waited for the lock, by a concurrent winner.
                lock (_gate)
                {
                    if (_channel is { IsOpen: true } channel)
                        return channel;
                }

                // null (never opened) or shut down: open it. The semaphore
                // guarantees exactly one opener at a time, and the re-check
                // above made it the only thread that can open now, so the
                // assignment cannot lose a rival's channel.
                _channel = await OpenChannelAsync().ConfigureAwait(false);
            }
            finally
            {
                _openLock.Release();
            }
        }
    }

    /// <summary>
    /// Opens a fresh channel for a single operation; the CALLER disposes it.
    /// Ack/publish/admin work uses this so a shared channel the broker shut
    /// down between calls can never be handed out.
    /// </summary>
    public virtual Task<IChannel> CreateChannelAsync()
        => OpenChannelAsync();

    /// <summary>
    /// Opens a new connection and channel; the channel's broker-side
    /// shutdown clears the cache (if it is the cached one).
    /// </summary>
    protected virtual async Task<IChannel> OpenChannelAsync()
    {
        var connectionString = _configuration.GetConnectionString("RabbitMq") ?? "amqp://localhost";
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

        IConnection? connection = null;
        try
        {
            connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
            var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(false, false)).ConfigureAwait(false);
            channel.ChannelShutdownAsync += (_, _) =>
            {
                lock (_gate)
                {
                    _channel = null;
                }

                return Task.CompletedTask;
            };

            return channel;
        }
        catch
        {
            if (connection is not null)
                await connection.DisposeAsync();
            throw;
        }
    }
}
