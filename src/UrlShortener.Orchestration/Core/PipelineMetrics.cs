namespace UrlShortener.Orchestration.Core;

public class PipelineMetrics
{
    public Guid RunId { get; init; }
    public string Scenario { get; init; } = string.Empty;

    // All four metrics derived from audit log — no separate tracking system
    public double SuccessRatePercent { get; init; }       // completions / total stage entries × 100
    public double RetryFrequency { get; init; }           // retries / total stage entries
    public int TotalRollbacks { get; init; }              // RolledBack events
    public TimeSpan? MeanTimeToRecovery { get; init; }    // avg (StageFailed → next StageCompleted) per stage
    public TimeSpan? EndToEndLatency { get; init; }       // PipelineCompleted − PipelineStarted

    // Raw counts exposed for transparency
    public int TotalStageRuns { get; init; }
    public int FirstAttemptSuccesses { get; init; }
    public int TotalRetries { get; init; }
    public int TotalStageCompletions { get; init; }
}
