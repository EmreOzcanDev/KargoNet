using KargoNet.Contracts.Models;

namespace KargoNet.Shipment.Api.Entities;

public class Shipment
{
    public Guid Id { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Destination { get; set; } = string.Empty;

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Created;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
