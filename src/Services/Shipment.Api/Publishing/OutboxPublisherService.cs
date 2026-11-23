using System.Text;
using KargoNet.Messaging.Publishing;
using KargoNet.Messaging.Topology;
using KargoNet.Shipment.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KargoNet.Shipment.Api.Publishing;

using OutboxMessage = KargoNet.Shipment.Api.Entities.OutboxMessage;
using OutboxStatus = KargoNet.Shipment.Api.Entities.OutboxStatus;

/// <summary>
/// Polls the outbox for Pending rows (50 per batch, oldest first) every 2 s and
/// publishes each stored envelope VERBATIM to routing key <c>shipment</c> with a
/// broker confirm. A confirmed row is marked Published + PublishedAt in its own
/// short transaction; any failure leaves the row Pending (never deleted) so the
/// next poll retries it.
/// </summary>
public class OutboxPublisherService : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessagePublisher _publisher;
    private readonly ILogger<OutboxPublisherService> _logger;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        MessagePublisher publisher,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// One poll batch: publish up to 50 Pending rows, oldest first. Returns the
    /// number of rows published. Directly drivable — no timer or delay logic.
    /// </summary>
    public async Task<int> PollOnceAsync(ShipmentDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Set<OutboxMessage>()
            .Where(o => o.Status == OutboxStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // The stored envelope JSON crosses to the broker verbatim —
                // never re-serialized.
                await _publisher.PublishAsync(
                    RabbitTopology.ShipmentRoutingKey,
                    Encoding.UTF8.GetBytes(row.PayloadJson),
                    null);

                var now = DateTimeOffset.UtcNow;
                row.Status = OutboxStatus.Published;
                row.PublishedAt = now;

                // One row per own short transaction: a single failure cannot
                // block the rest of the batch.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Leave the row Pending for the next poll; never delete. No
                // ChangeTracker.Clear() here: clearing the whole tracker would
                // detach the remaining rows in this batch, so their Published
                // marks could never persist (duplicate delivery on retry).
                _logger.LogWarning(ex, "Outbox publish failed for row {RowId}; leaving Pending for retry.", row.Id);
            }
        }

        return published;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The DbContext is scoped: a fresh one per poll keeps the
                // change tracker from growing for the life of the host.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
                await PollOnceAsync(db, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A database failure (Postgres unreachable/restarting) must not
                // escape ExecuteAsync — that would stop the host and take the
                // REST API down with it. Log and retry next interval.
                _logger.LogError(ex, "Outbox poll failed; retrying next interval.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
