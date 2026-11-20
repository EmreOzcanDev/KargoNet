namespace KargoNet.Tracking.Api.Tracking;

/// <summary>
/// Broadcast payload for a tracking update (camelCase JSON): the post-update
/// read-model row. Same shape as the GET-endpoint <c>TrackingDto</c>
/// (plan 04-02).
/// </summary>
public record TrackingUpdatedDto(
    string trackingNumber,
    string description,
    string destination,
    string status,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);
