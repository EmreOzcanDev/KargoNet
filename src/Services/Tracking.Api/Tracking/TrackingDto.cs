namespace KargoNet.Tracking.Api.Tracking;

/// <summary>
/// Cache value and GET-endpoint response shape (camelCase JSON): the
/// read-model row for one tracking number. Same fields as
/// <see cref="TrackingUpdatedDto"/> (the broadcast payload).
/// </summary>
public record TrackingDto(
    string trackingNumber,
    string description,
    string destination,
    string status,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);
