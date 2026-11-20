namespace KargoNet.Tracking.Api.Entities;

/// <summary>
/// Read model of a shipment maintained by the tracking consumer: one row per
/// tracking number, upserted by <c>shipment.created</c> / <c>shipment.status.changed</c>.
/// Status is a string, mirroring the Shipment.Api convention.
/// </summary>
public class ShipmentTracking
{
    public string TrackingNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string Status { get; set; } = "Created";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
