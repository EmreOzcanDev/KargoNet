namespace KargoNet.Messaging.Consuming;

/// <summary>
/// Pure retry decision: a delivery that has already failed <paramref name="currentRetryCountHeader"/>
/// times gets one more attempt up to <see cref="MaxAttempts"/> total tries, then the DLQ.
/// </summary>
public static class RetryPolicy
{
    /// <summary>Total attempts before dead-lettering: the first try plus <see cref="MaxRetries"/> republished tries.</summary>
    public const int MaxRetries = 2;

    public static RetryDecision Decide(int currentRetryCountHeader, Exception exception)
    {
        var attempt = currentRetryCountHeader + 1;

        if (attempt <= MaxRetries)
            return new RetryDecision.RepublishToRetry(currentRetryCountHeader + 1);

        return new RetryDecision.RouteToDlq();
    }
}
