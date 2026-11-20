namespace KargoNet.Tracking.Api.Dlq;

/// <summary>
/// Admin surface over the dead-letter queue <c>shipments.dlq</c>.
/// </summary>
public interface IDlqAdmin
{
    /// <summary>Number of messages currently sitting in the DLQ.</summary>
    Task<int> CountAsync();

    /// <summary>
    /// Moves every message from the DLQ back to
    /// <c>kargonet.shipments</c> under the <c>shipment</c> routing key (a
    /// fresh delivery with NO <c>x-retry-count</c> header) and returns how
    /// many were moved. Honors <paramref name="ct"/>: a cancelled request
    /// stops the drain before the next message is read.
    /// </summary>
    Task<int> ReplayAsync(CancellationToken ct);
}
