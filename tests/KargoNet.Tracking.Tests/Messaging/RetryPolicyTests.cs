using KargoNet.Messaging.Consuming;

namespace KargoNet.Tracking.Tests.Messaging;

public class RetryPolicyTests
{
    private static Exception Exception() => new InvalidOperationException("boom");

    [Fact]
    public void FirstFailure_RepublishesWithRetryCountOne()
    {
        var decision = RetryPolicy.Decide(0, Exception());

        var republish = Assert.IsType<RetryDecision.RepublishToRetry>(decision);
        Assert.Equal(1, republish.NewRetryCount);
    }

    [Fact]
    public void SecondFailure_RepublishesWithRetryCountTwo()
    {
        var decision = RetryPolicy.Decide(1, Exception());

        var republish = Assert.IsType<RetryDecision.RepublishToRetry>(decision);
        Assert.Equal(2, republish.NewRetryCount);
    }

    [Fact]
    public void ThirdFailure_RoutesToDlq()
    {
        var decision = RetryPolicy.Decide(2, Exception());

        Assert.IsType<RetryDecision.RouteToDlq>(decision);
    }

    [Fact]
    public void FourthFailure_RoutesToDlq()
    {
        var decision = RetryPolicy.Decide(3, Exception());

        Assert.IsType<RetryDecision.RouteToDlq>(decision);
    }
}
