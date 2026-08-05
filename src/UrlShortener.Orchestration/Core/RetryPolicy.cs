namespace UrlShortener.Orchestration.Core;

public class RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(2);

    // Produces 2s, 4s, 8s for attempts 1, 2, 3
    public TimeSpan GetDelay(int attemptNumber) =>
        TimeSpan.FromSeconds(Math.Pow(2, attemptNumber) * BackoffBase.TotalSeconds / 2);
}
