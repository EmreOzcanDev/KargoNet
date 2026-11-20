using System.Text.Json;
using StackExchange.Redis;
using System.Text.Json.Serialization;
using KargoNet.Tracking.Api.Infra;

namespace KargoNet.Tracking.Api.Tracking;

/// <summary>
/// <see cref="ITrackingCache"/> on StackExchange.Redis: key
/// <c>tracking:{trackingNumber-lowercased}</c>, value the camelCase JSON of
/// <see cref="TrackingUpdatedDto"/>, caller-supplied TTL (the read path uses
/// 10 minutes). Connects lazily through <see cref="LazyRedisConnection"/> —
/// nothing touches Redis until the first cache call.
/// </summary>
public sealed class RedisTrackingCache : ITrackingCache
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly LazyRedisConnection _connections;

    public RedisTrackingCache(LazyRedisConnection connections)
        => _connections = connections;

    public async Task<TrackingUpdatedDto?> GetAsync(string trackingNumber, CancellationToken ct)
    {
        var db = (await _connections.GetAsync().ConfigureAwait(false)).GetDatabase();
        var value = await db.StringGetAsync(Key(trackingNumber)).ConfigureAwait(false);
        if (value.IsNull)
            return null;

        try
        {
            return JsonSerializer.Deserialize<TrackingUpdatedDto>(value.ToString(), Options);
        }
        catch (JsonException)
        {
            // Corrupt payload: drop the key so the next read repopulates
            // from EF instead of failing on the same value forever.
            await db.KeyDeleteAsync(Key(trackingNumber)).ConfigureAwait(false);
            return null;
        }
    }

    public async Task SetAsync(TrackingUpdatedDto dto, TimeSpan ttl, CancellationToken ct)
    {
        var db = (await _connections.GetAsync().ConfigureAwait(false)).GetDatabase();
        var json = JsonSerializer.Serialize(dto, Options);
        await db.StringSetAsync(Key(dto.trackingNumber), json, ttl).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string trackingNumber, CancellationToken ct)
    {
        var db = (await _connections.GetAsync().ConfigureAwait(false)).GetDatabase();
        await db.KeyDeleteAsync(Key(trackingNumber)).ConfigureAwait(false);
    }

    /// <summary>The fixed cache key for a tracking number (case-insensitive).</summary>
    public static string Key(string trackingNumber)
        => $"tracking:{trackingNumber.ToLowerInvariant()}";

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
