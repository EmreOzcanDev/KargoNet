namespace KargoNet.Contracts.Events;

public record ShipmentCreatedPayload(
    string TrackingNumber,
    string Description,
    string Destination);
