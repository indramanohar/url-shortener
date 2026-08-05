using UrlShortener.Orchestration.AuditLog;

namespace UrlShortener.Orchestration.Core;

public static class MetricsCalculator
{
    public static PipelineMetrics Calculate(Guid runId, string scenario, IReadOnlyList<AuditEntry> entries)
    {
        var totalRuns = entries.Count(e =>
            e.EventType == AuditEventType.StageEntered && e.StageName != "Pipeline");

        var totalCompletions = entries.Count(e =>
            e.EventType == AuditEventType.StageCompleted && e.StageName != "Pipeline");

        var totalRetries   = entries.Count(e => e.EventType == AuditEventType.RetryAttempt);
        var totalRollbacks = entries.Count(e => e.EventType == AuditEventType.RolledBack);

        var firstAttemptSuccesses = ComputeFirstAttemptSuccesses(entries);

        var mttr    = ComputeMttr(entries);
        var latency = ComputeLatency(entries);

        return new PipelineMetrics
        {
            RunId                  = runId,
            Scenario               = scenario,
            TotalStageRuns         = totalRuns,
            TotalStageCompletions  = totalCompletions,
            TotalRetries           = totalRetries,
            TotalRollbacks         = totalRollbacks,
            FirstAttemptSuccesses  = firstAttemptSuccesses,
            SuccessRatePercent     = totalRuns > 0 ? (double)totalCompletions / totalRuns * 100 : 0,
            RetryFrequency         = totalRuns > 0 ? (double)totalRetries / totalRuns : 0,
            MeanTimeToRecovery     = mttr,
            EndToEndLatency        = latency
        };
    }

    // Walk entries in order. RetryAttempt marks the stage as "had a retry" — that flag
    // survives the subsequent StageEntered (the retry entry) and clears only on
    // StageCompleted. Works correctly with parallel stages because each stage has its own
    // slot in the set.
    private static int ComputeFirstAttemptSuccesses(IReadOnlyList<AuditEntry> entries)
    {
        var pendingRetry = new HashSet<string>(); // stages that had at least one retry in their current run
        int count = 0;

        foreach (var e in entries)
        {
            switch (e.EventType)
            {
                case AuditEventType.RetryAttempt:
                    pendingRetry.Add(e.StageName);
                    break;

                case AuditEventType.StageCompleted when e.StageName != "Pipeline":
                    if (!pendingRetry.Contains(e.StageName))
                        count++;
                    pendingRetry.Remove(e.StageName);
                    break;
            }
        }
        return count;
    }

    // MTTR: for each StageFailed, find the next StageCompleted for that same stage.
    // Average the recovery deltas.
    private static TimeSpan? ComputeMttr(IReadOnlyList<AuditEntry> entries)
    {
        var recoveryTimes = new List<TimeSpan>();
        var failedAt = new Dictionary<string, DateTime>();

        foreach (var e in entries)
        {
            if (e.EventType == AuditEventType.StageFailed && e.StageName != "Pipeline")
            {
                failedAt[e.StageName] = e.Timestamp;
            }
            else if (e.EventType == AuditEventType.StageCompleted && failedAt.TryGetValue(e.StageName, out var ft))
            {
                recoveryTimes.Add(e.Timestamp - ft);
                failedAt.Remove(e.StageName);
            }
        }

        return recoveryTimes.Count > 0
            ? TimeSpan.FromSeconds(recoveryTimes.Average(t => t.TotalSeconds))
            : null;
    }

    private static TimeSpan? ComputeLatency(IReadOnlyList<AuditEntry> entries)
    {
        var started   = entries.FirstOrDefault(e => e.EventType == AuditEventType.PipelineStarted)?.Timestamp;
        var completed = entries.FirstOrDefault(e => e.EventType == AuditEventType.PipelineCompleted)?.Timestamp;
        return started.HasValue && completed.HasValue ? completed.Value - started.Value : null;
    }
}
