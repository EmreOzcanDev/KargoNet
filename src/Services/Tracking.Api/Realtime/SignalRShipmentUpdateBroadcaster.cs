using KargoNet.Tracking.Api.Tracking;
using Microsoft.AspNetCore.SignalR;

namespace KargoNet.Tracking.Api.Realtime;

/// <summary>
/// <see cref="IShipmentUpdateBroadcaster"/> over
/// <c>IHubContext&lt;TrackingHub&gt;</c> (TRK-03 / design D4): every applied
/// update is sent as a <c>ShipmentUpdated</c> message to ALL connected
/// clients and to the group named after the tracking number. Only the
/// already-published <see cref="TrackingUpdatedDto"/> is sent — no secrets
/// (threat T-04-07).
/// </summary>
public sealed class SignalRShipmentUpdateBroadcaster : IShippingUpdateBroadcaster
{
    private readonly IHubContext<TrackingHub> _hub;

    public SignalRShipmentUpdateBroadcaster(IHubContext<TrackingHub> hub)
        => _hub = hub;

    public async Task BroadcastAsync(TrackingUpdatedDto dto, CancellationToken ct)
    {
        await _hub.Clients.All
            .SendAsync("ShipmentUpdated", dto, ct)
            .ConfigureAwait(false);

        await _hub.Clients.Group(dto.trackingNumber)
            .SendAsync("ShipmentUpdated", dto, ct)
            .ConfigureAwait(false);
    }
}
