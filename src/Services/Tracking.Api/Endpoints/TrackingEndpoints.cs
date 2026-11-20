using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using KargoNet.Tracking.Api.Tracking;

namespace KargoNet.Tracking.Api.Endpoints;

/// <summary>
/// Read endpoints of the Tracking API:
/// <list type="bullet">
/// <item><c>GET /api/tracking/{trackingNumber}</c> — cache-aside: a hit returns
/// the cached DTO without touching EF; a miss loads the row (404 if absent),
/// writes the cache with a 10-minute TTL, and returns 200.</item>
/// <item><c>GET /api/tracking/{trackingNumber}/events</c> — the event timeline
/// from <see cref="TrackingEvent"/> rows ordered by <c>OccurredAt</c>, with NO
/// caching (the timeline grows; it is always read fresh).</item>
/// </list>
/// </summary>
public static class TrackingEndpoints
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapTrackingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tracking/{trackingNumber}", async (
            string trackingNumber,
            ITrackingCache cache,
            TrackingDbContext db,
            CancellationToken ct) =>
        {
            var cached = await cache.GetAsync(trackingNumber, ct).ConfigureAwait(false);
            if (cached is not null)
                return Results.Ok(ToDto(cached));

            var row = await db.ShipmentTrackings
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TrackingNumber == trackingNumber, ct)
                .ConfigureAwait(false);
            if (row is null)
                return Results.NotFound();

            var dto = ToDto(row);
            await cache.SetAsync(ToUpdated(dto), CacheTtl, ct).ConfigureAwait(false);
            return Results.Ok(dto);
        });

        endpoints.MapGet("/api/tracking/{trackingNumber}/events", async (
            string trackingNumber,
            TrackingDbContext db,
            CancellationToken ct) =>
        {
            var timeline = await db.TrackingEvents
                .AsNoTracking()
                .Where(e => e.TrackingNumber == trackingNumber)
                .OrderBy(e => e.OccurredAt)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return Results.Ok(timeline);
        });

        return endpoints;
    }

    private static TrackingDto ToDto(ShipmentTracking row) =>
        new(row.TrackingNumber, row.Description, row.Destination, row.Status, row.CreatedAt, row.UpdatedAt);

    private static TrackingDto ToDto(TrackingUpdatedDto dto) =>
        new(dto.trackingNumber, dto.description, dto.destination, dto.status, dto.createdAt, dto.updatedAt);

    private static TrackingUpdatedDto ToUpdated(TrackingDto dto) =>
        new(dto.trackingNumber, dto.description, dto.destination, dto.status, dto.createdAt, dto.updatedAt);
}
