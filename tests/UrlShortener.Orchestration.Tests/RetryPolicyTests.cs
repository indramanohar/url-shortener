using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Tests;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void GetDelay_ProducesExponentialBackoff(int attempt, int expectedSeconds)
    {
        var policy = new RetryPolicy { MaxAttempts = 3, BackoffBase = TimeSpan.FromSeconds(2) };
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.GetDelay(attempt));
    }

    [Fact]
    public void DefaultPolicy_HasMaxAttempts3()
    {
        var policy = new RetryPolicy();
        Assert.Equal(3, policy.MaxAttempts);
    }
}
