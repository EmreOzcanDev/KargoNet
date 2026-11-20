using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace KargoNet.Tracking.Api.Infra;

/// <summary>
/// Opens the Redis connection on FIRST use and caches it afterwards (mirrors
/// <c>KargoNet.Messaging.Publishing.LazyOutboxChannelFactory</c>). Nothing
/// connects at startup, so the API starts and serves traffic without a
/// reachable Redis; a missing server surfaces on the first cache call
/// (live Redis is a Phase 6 deferral).
/// </summary>
public sealed class LazyRedisConnection
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private IConnectionMultiplexer? _connection;

    public LazyRedisConnection(IConfiguration configuration)
        => _configuration = configuration;

    public async Task<IConnectionMultiplexer> GetAsync()
    {
        // Guard the check-then-act across the await: concurrent first calls
        // must open exactly one connection (the loser re-checks under the
        // lock instead of leaking its own).
        if (_connection is null)
        {
            await _openLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _connection ??= await OpenAsync().ConfigureAwait(false);
            }
            finally
            {
                _openLock.Release();
            }
        }

        return _connection;
    }

    private async Task<IConnectionMultiplexer> OpenAsync()
    {
        var configuration = _configuration.GetConnectionString("Redis") ?? "localhost";
        return await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false);
    }
}
