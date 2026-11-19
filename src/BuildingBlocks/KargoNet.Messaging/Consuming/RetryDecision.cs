namespace KargoNet.Messaging.Consuming;

/// <summary>
/// Where a failed delivery goes: republished to the retry queue with a bumped
/// retry count, or routed to the dead-letter queue.
/// </summary>
public abstract record RetryDecision
{
    /// <summary>Republish to the retry queue with the next attempt's retry count.</summary>
    public sealed record RepublishToRetry(int NewRetryCount) : RetryDecision;

    /// <summary>Route to the dead-letter queue — no further attempts.</summary>
    public sealed record RouteToDlq : RetryDecision;
}
