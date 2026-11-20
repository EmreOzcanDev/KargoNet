using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;
using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Entities;
using KargoNet.Tracking.Api.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KargoNet.Tracking.Api.Consuming;

/// <summary>
/// Applies shipment events to the tracking read model idempotently.
/// One EF transaction per apply: <c>ProcessedEvents</c> lookup by
/// <c>EventId</c>, <c>ShipmentTracking</c> upsert, <c>TrackingEvent</c>
/// insert, <c>ProcessedEvent</c> insert. A concurrent-duplicate
/// <see cref="DbUpdateException"/> on commit is treated as
/// already-processed ONLY when the marker re-check confirms the race (D2
/// backstop): after the rollback the <c>ProcessedEvents</c> row is re-looked
/// up, and a missing marker means a genuine write failure, which is
/// rethrown so the consumer routes the event to retry → DLQ like every
/// other failure. Post-commit side effects (cache invalidation +
/// broadcast) are best-effort: each is logged and never rethrown.
/// </summary>
public sealed class ShipmentEventHandler
{
    private readonly TrackingDbContext _db;
    private readonly ITrackingCache _cache;
    private readonly IShippingUpdateBroadcaster _broadcaster;
    private readonly ILogger<ShipmentEventHandler> _logger;

    public ShipmentEventHandler(
        TrackingDbContext db,
        ITrackingCache cache,
        IShippingUpdateBroadcaster broadcaster,
        ILogger<ShipmentEventHandler> logger)
    {
        _db = db;
        _cache = cache;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public Task ApplyCreatedAsync(
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        string payloadJson,
        ShipmentCreatedPayload payload)
        => ApplyCoreAsync(eventId, payload.TrackingNumber, eventType, payloadJson, occurredAt, row =>
        {
            row.Description = payload.Description;
            row.Destination = payload.Destination;
            row.Status = "Created";
            row.CreatedAt = occurredAt;
            row.UpdatedAt = occurredAt;
        });

    public Task ApplyStatusChangedAsync(
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        string payloadJson,
        ShipmentStatusChangedPayload payload)
        => ApplyCoreAsync(eventId, payload.TrackingNumber, eventType, payloadJson, occurredAt, row =>
        {
            row.Status = payload.ToStatus.ToString();
            row.UpdatedAt = occurredAt;
        });

    private async Task ApplyCoreAsync(
        Guid eventId,
        string trackingNumber,
        string eventType,
        string payloadJson,
        DateTimeOffset occurredAt,
        Action<ShipmentTracking> mutate)
    {
        ShipmentTracking row = null!;
        var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // (1) Deterministic idempotency: already processed -> skip (success).
            var alreadyProcessed = await _db.ProcessedEvents
                .AnyAsync(e => e.EventId == eventId);
            if (alreadyProcessed)
            {
                await transaction.RollbackAsync();
                _logger.LogDebug("Event {EventId} already processed; skipping.", eventId);
                return;
            }

            // (2) Upsert the read-model row by tracking number.
            row = (await _db.ShipmentTrackings
                .FirstOrDefaultAsync(t => t.TrackingNumber == trackingNumber))!;
            if (row is null)
            {
                row = new ShipmentTracking
                {
                    TrackingNumber = trackingNumber,
                    CreatedAt = occurredAt,
                    UpdatedAt = occurredAt,
                };
                _db.ShipmentTrackings.Add(row);
            }
            mutate(row);

            // (3) Append the tracking timeline entry.
            _db.TrackingEvents.Add(new TrackingEvent
            {
                EventId = eventId,
                TrackingNumber = trackingNumber,
                EventType = eventType,
                PayloadJson = payloadJson,
                OccurredAt = occurredAt,
            });

            // (4) Record the processed marker.
            _db.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = eventId,
                ProcessedAt = DateTimeOffset.Now,
            });

            // (5) + (6) Persist and commit.
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex)
        {
            // The D2 backstop is the ProcessedEvent PK race, not a blanket
            // "any write failure is success": re-check the marker in the
            // store after the rollback. A concurrent writer that committed
            // it means this event was already applied — swallow. Anything
            // else (foreign-key violation, value too long, …) rethrows so
            // the consumer routes it to retry → DLQ like every other
            // failure.
            await transaction.RollbackAsync();
            var alreadyProcessed = await _db.ProcessedEvents
                .AsNoTracking()
                .AnyAsync(e => e.EventId == eventId);

            if (!alreadyProcessed)
                throw;

            _logger.LogInformation(
                ex,
                "Concurrent duplicate race for {EventId} on {TrackingNumber}; treating as already processed.",
                eventId, trackingNumber);
        }

        // Post-commit side effects: best-effort, never rethrow.
        try
        {
            await _cache.DeleteAsync(trackingNumber, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for {TrackingNumber}; continuing.", trackingNumber);
        }

        try
        {
            var dto = new TrackingUpdatedDto(
                row.TrackingNumber,
                row.Description,
                row.Destination,
                row.Status,
                row.CreatedAt,
                row.UpdatedAt);
            await _broadcaster.BroadcastAsync(dto, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Broadcast failed for {TrackingNumber}; continuing.", trackingNumber);
        }
    }
}
