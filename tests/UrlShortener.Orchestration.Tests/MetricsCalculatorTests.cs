using UrlShortener.Orchestration.AuditLog;
using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Tests;

public class MetricsCalculatorTests
{
    private static async Task<HashChainedAuditLog> BuildLog(
        params (AuditEventType type, string stage, string msg)[] events)
    {
        var log = new HashChainedAuditLog();
        foreach (var (type, stage, msg) in events)
            await log.AppendAsync(type, stage, msg);
        return log;
    }

    [Fact]
    public async Task SuccessRate_Is100_WhenAllPassFirstAttempt()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted,  "Pipeline",       "start"),
            (AuditEventType.StageEntered,     "Requirements",   "enter"),
            (AuditEventType.StageCompleted,   "Requirements",   "done"),
            (AuditEventType.StageEntered,     "Design",         "enter"),
            (AuditEventType.StageCompleted,   "Design",         "done"),
            (AuditEventType.PipelineCompleted,"Pipeline",       "done"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);

        Assert.Equal(100.0, m.SuccessRatePercent, precision: 1);
        Assert.Equal(0, m.TotalRetries);
        Assert.Equal(2, m.FirstAttemptSuccesses);
    }

    [Fact]
    public async Task SuccessRate_IsLower_WhenRetryRequired()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted,  "Pipeline",  "start"),
            (AuditEventType.StageEntered,     "Testing",   "enter"),
            (AuditEventType.StageFailed,      "Testing",   "fail"),
            (AuditEventType.RetryScheduled,   "Testing",   "retry sched"),
            (AuditEventType.RetryAttempt,     "Testing",   "retry 2"),
            (AuditEventType.StageEntered,     "Testing",   "enter again"),
            (AuditEventType.StageCompleted,   "Testing",   "done"),
            (AuditEventType.PipelineCompleted,"Pipeline",  "done"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);

        // 2 StageEntered, 1 StageCompleted → 50%
        Assert.Equal(50.0, m.SuccessRatePercent, precision: 1);
        Assert.Equal(1, m.TotalRetries);
        Assert.Equal(0, m.FirstAttemptSuccesses); // the completion had a retry preceding it
    }

    [Fact]
    public async Task FirstAttemptSuccesses_CountsCorrectly_WithMixedStages()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted,  "Pipeline",       "start"),
            (AuditEventType.StageEntered,     "Requirements",   "enter"),
            (AuditEventType.StageCompleted,   "Requirements",   "done"),   // first attempt
            (AuditEventType.StageEntered,     "Testing",        "enter"),
            (AuditEventType.StageFailed,      "Testing",        "fail"),
            (AuditEventType.RetryAttempt,     "Testing",        "retry"),
            (AuditEventType.StageEntered,     "Testing",        "enter"),
            (AuditEventType.StageCompleted,   "Testing",        "done"),   // NOT first attempt
            (AuditEventType.PipelineCompleted,"Pipeline",       "done"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);

        Assert.Equal(1, m.FirstAttemptSuccesses); // only Requirements
    }

    [Fact]
    public async Task Mttr_IsNull_WhenNoFailures()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted,  "Pipeline", "start"),
            (AuditEventType.StageEntered,     "Design",   "enter"),
            (AuditEventType.StageCompleted,   "Design",   "done"),
            (AuditEventType.PipelineCompleted,"Pipeline", "done"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);
        Assert.Null(m.MeanTimeToRecovery);
    }

    [Fact]
    public async Task Mttr_IsPositive_WhenFailurePrecededSuccess()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted,  "Pipeline", "start"),
            (AuditEventType.StageEntered,     "Testing",  "enter"),
            (AuditEventType.StageFailed,      "Testing",  "fail"),
            (AuditEventType.RetryAttempt,     "Testing",  "retry"),
            (AuditEventType.StageEntered,     "Testing",  "enter"),
            (AuditEventType.StageCompleted,   "Testing",  "done"),
            (AuditEventType.PipelineCompleted,"Pipeline", "done"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);
        Assert.NotNull(m.MeanTimeToRecovery);
        Assert.True(m.MeanTimeToRecovery!.Value.TotalSeconds >= 0);
    }

    [Fact]
    public async Task EndToEndLatency_IsNull_WhenPipelineNotCompleted()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted, "Pipeline", "start"),
            (AuditEventType.StageEntered,    "Design",   "enter"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);
        Assert.Null(m.EndToEndLatency);
    }

    [Fact]
    public async Task TotalRollbacks_CountsRolledBackEvents()
    {
        var log = await BuildLog(
            (AuditEventType.PipelineStarted,  "Pipeline", "start"),
            (AuditEventType.RolledBack,       "Testing",  "rollback 1"),
            (AuditEventType.RolledBack,       "Testing",  "rollback 2"),
            (AuditEventType.PipelineCompleted,"Pipeline", "done"));

        var m = MetricsCalculator.Calculate(Guid.NewGuid(), "test", log.Entries);
        Assert.Equal(2, m.TotalRollbacks);
    }
}
