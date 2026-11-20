namespace Portal.Web.Services;

/// <summary>
/// Client for the two frozen upstream APIs (Shipment + Tracking).
/// 5xx responses and timeouts surface as <see cref="ApiRequestException"/>;
/// a 404 on GetTrackingAsync is a normal "not found" and returns null.
/// </summary>
public interface IPortalApiClient
{
    Task<TrackingDto?> GetTrackingAsync(string number, CancellationToken ct = default);
    Task<IReadOnlyList<TrackingEventDto>> GetTimelineAsync(string number, CancellationToken ct = default);
    Task<ShipmentDto> CreateShipmentAsync(CreateShipmentRequestDto request, CancellationToken ct = default);
}
