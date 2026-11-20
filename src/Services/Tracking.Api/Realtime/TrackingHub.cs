using Microsoft.AspNetCore.SignalR;

namespace KargoNet.Tracking.Api.Realtime;

/// <summary>
/// Real-time tracking hub (TRK-03 / design D4). A client joins the group
/// named after its tracking number so that
/// <see cref="SignalRShipmentUpdateBroadcaster"/> can target its
/// connection directly, in addition to <c>Clients.All</c>.
/// </summary>
public class TrackingHub : Hub
{
    public Task JoinTrackingGroup(string trackingNumber)
        => Groups.AddToGroupAsync(Context.ConnectionId, trackingNumber);

    public Task LeaveTrackingGroup(string trackingNumber)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, trackingNumber);
}
