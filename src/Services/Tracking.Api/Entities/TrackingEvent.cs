namespace KargoNet.Tracking.Api.Entities;

/// <summary>
/// One applied shipment event, kept as the tracking timeline feed (Phases 5).
/// </summary>
public class TrackingEvent
{
    public Guid EventId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}
