namespace KargoNet.Tracking.Api.Tracking;

/// <summary>
/// Broadcasts a tracking update to realtime subscribers. The
/// <c>IHubContext&lt;TrackingHub&gt;</c> implementation lands in plan 04-03.
/// </summary>
public interface IShippingUpdateBroadcaster
{
    Task BroadcastAsync(TrackingUpdatedDto dto, CancellationToken ct);
}
