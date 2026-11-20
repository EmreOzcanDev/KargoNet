
namespace KargoNet.Tracking.Api.Tracking;

/// <summary>
/// Cache-aside seam for the tracking read model. The Redis implementation
/// lands in plan 04-02; the value type is the <see cref="TrackingUpdatedDto"/>
/// shape (identical fields to the GET-endpoint <c>TrackingDto</c> that lands
/// in plan 04-02).
/// </summary>
public interface ITrackingCache
{
    Task<TrackingUpdatedDto?> GetAsync(string trackingNumber, CancellationToken ct);

    Task SetAsync(TrackingUpdatedDto dto, TimeSpan ttl, CancellationToken ct);

    Task DeleteAsync(string trackingNumber, CancellationToken ct);
}
