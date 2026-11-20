namespace Portal.Web.Services;

/// <summary>Mirrors the frozen Shipment.Api response (camelCase JSON).</summary>
public record ShipmentDto(
    Guid id,
    string trackingNumber,
    string? description,
    string destination,
    string status,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);

/// <summary>Mirrors the frozen Shipment.Api create request body.</summary>
public record CreateShipmentRequestDto(
    string? trackingNumber,
    string? description,
    string destination);

/// <summary>Mirrors the frozen Tracking.Api tracking response (camelCase JSON).</summary>
public record TrackingDto(
    string trackingNumber,
    string description,
    string destination,
    string status,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);

/// <summary>Mirrors the frozen Tracking.Api event feed row (camelCase JSON).</summary>
public record TrackingEventDto(
    Guid eventId,
    string trackingNumber,
    string eventType,
    string payloadJson,
    DateTimeOffset occurredAt);
