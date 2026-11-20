namespace KargoNet.Shipment.Api.Dto
{
    using Shipment = KargoNet.Shipment.Api.Entities.Shipment;

    public record ShipmentDto(
        Guid Id,
        string TrackingNumber,
        string? Description,
        string Destination,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public static ShipmentDto From(Shipment shipment)
            => new(
                shipment.Id,
                shipment.TrackingNumber,
                shipment.Description,
                shipment.Destination,
                shipment.Status.ToString(),
                shipment.CreatedAt,
                shipment.UpdatedAt);
    }

    public record CreateShipmentRequest(
        string? TrackingNumber,
        string? Description,
        string? Destination);

    public record UpdateStatusRequest(
        string? Status);
}
