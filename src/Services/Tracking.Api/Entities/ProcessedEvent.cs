namespace KargoNet.Tracking.Api.Entities;

/// <summary>
/// Idempotency marker: one row per applied event <see cref="EventId"/>. A
/// redelivered event whose marker already exists is skipped as a success.
/// </summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
