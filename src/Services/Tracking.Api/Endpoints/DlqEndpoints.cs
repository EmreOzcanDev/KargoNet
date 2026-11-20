using KargoNet.Tracking.Api.Dlq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace KargoNet.Tracking.Api.Endpoints;

/// <summary>
/// Minimal operator surface for the dead-letter queue:
/// <list type="bullet">
/// <item><c>GET /api/admin/dlq</c> → <c>{ "count": n }</c></item>
/// <item><c>POST /api/admin/dlq/replay</c> → <c>{ "replayed": n }</c></item>
/// </list>
/// </summary>
public static class DlqEndpoints
{
    public static IEndpointRouteBuilder MapDlqEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/dlq", async (IDlqAdmin dlq, CancellationToken ct) =>
            Results.Ok(new { count = await dlq.CountAsync().ConfigureAwait(false) }));

        endpoints.MapPost("/api/admin/dlq/replay", async (IDlqAdmin dlq, CancellationToken ct) =>
            Results.Ok(new { replayed = await dlq.ReplayAsync(ct).ConfigureAwait(false) }));

        return endpoints;
    }
}
