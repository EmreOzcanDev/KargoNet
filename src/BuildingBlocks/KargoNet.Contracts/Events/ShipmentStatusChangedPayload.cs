using KargoNet.Contracts.Models;

namespace KargoNet.Contracts.Events;

public record ShipmentStatusChangedPayload(
    Guid ShipmentId,
    string TrackingNumber,
    ShipmentStatus FromStatus,
    ShipmentStatus ToStatus);
