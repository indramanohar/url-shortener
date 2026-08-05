using UrlShortener.Orchestration.AuditLog;
using UrlShortener.Orchestration.Stages;

namespace UrlShortener.Orchestration.Core;

public class PipelineOrchestrator(
    IPipelineApprovalService approvalService,
    RetryPolicy? retryPolicy = null,
    TimeSpan? approvalTimeout = null)
{
    private readonly RetryPolicy _retry = retryPolicy ?? new RetryPolicy();
    private readonly TimeSpan _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(10);

    public async Task<PipelineRunResult> RunAsync(PipelineContext context)
    {
        var log = context.AuditLog;
        var ct = context.CancellationToken;

        await log.AppendAsync(AuditEventType.PipelineStarted, "Pipeline",
            $"Starting pipeline. Scenario={context.Scenario} RunId={context.RunId}");

        // ── Sequential: Requirements → Design → Implementation ──
        var sequential = new IPipelineStage[]
        {
            new RequirementsStage(),
            new DesignStage(),
            new ImplementationStage()
        };

        foreach (var stage in sequential)
        {
            ct.ThrowIfCancellationRequested();
            var ok = await RunStageAsync(context, stage, ct);
            if (!ok.Succeeded)
                return await SafeStop(log, ok.FailureReason ?? $"{stage.Name} failed");
        }

        // ── Parallel branch: Testing ‖ Documentation ──
        // Testing failure after max retries → rollback to Implementation (back-edge).
        // Safe-stop if 2 rollbacks have been attempted (prevents infinite loops).
        int rollbackCount = 0;
        bool parallelPassed = false;

        while (!parallelPassed)
        {
            ct.ThrowIfCancellationRequested();

            var testingTask = RunStageWithRetryAsync(context, new TestingStage(), ct);
            var docsTask = RunStageWithRetryAsync(context, new DocumentationStage(), ct);

            // Real parallel execution via Task.WhenAll
            var results = await Task.WhenAll(testingTask, docsTask);
            var testingResult = results[0];
            var docsResult = results[1];

            if (testingResult.Succeeded && docsResult.Succeeded)
            {
                parallelPassed = true;
            }
            else if (!docsResult.Succeeded)
            {
                return await SafeStop(log, $"Documentation failed: {docsResult.FailureReason}");
            }
            else
            {
                // Testing exhausted retries → rollback
                rollbackCount++;
                if (rollbackCount > 1)
                    return await SafeStop(log, "Testing failed after rollback; safe-stopping.");

                await log.AppendAsync(AuditEventType.RolledBack, "Testing",
                    $"Testing failed after {_retry.MaxAttempts} attempts. " +
                    $"Rolling back to Implementation (rollback #{rollbackCount}). " +
                    $"Failure context: {testingResult.FailureReason}",
                    new Dictionary<string, string> { ["rollback_count"] = rollbackCount.ToString() });

                context.Artifacts["testing_failure_context"] = testingResult.FailureReason ?? "unknown";

                ct.ThrowIfCancellationRequested();
                var rollbackImpl = await RunStageAsync(context, new ImplementationStage(), ct);
                if (!rollbackImpl.Succeeded)
                    return await SafeStop(log, $"Implementation (rollback) failed: {rollbackImpl.FailureReason}");
            }
        }

        // ── Release gate (human approval) ──
        ct.ThrowIfCancellationRequested();
        var releaseResult = await RunStageAsync(context, new ReleaseStage(), ct);
        if (!releaseResult.Succeeded)
            return await SafeStop(log, releaseResult.FailureReason ?? "Release failed");

        await log.AppendAsync(AuditEventType.PipelineCompleted, "Pipeline",
            $"Pipeline completed successfully. Scenario={context.Scenario}",
            new Dictionary<string, string> { ["rollback_count"] = rollbackCount.ToString() });

        return PipelineRunResult.Completed();
    }

    // ── Stage runner: entry gate → execute → [human approval if required] → exit gate ──
    private async Task<StageResult> RunStageAsync(PipelineContext context, IPipelineStage stage,
        CancellationToken ct)
    {
        var log = context.AuditLog;

        await log.AppendAsync(AuditEventType.StageEntered, stage.Name, $"Entering {stage.Name}");

        var entry = await stage.EntryGate(context);
        if (!entry.Passed)
        {
            await log.AppendAsync(AuditEventType.StageFailed, stage.Name,
                $"Entry gate failed: {entry.Reason}");
            return StageResult.Fail(entry.Reason!);
        }

        var result = await stage.Execute(context);
        if (!result.Succeeded)
        {
            await log.AppendAsync(AuditEventType.StageFailed, stage.Name,
                $"Execution failed: {result.FailureReason}");
            return result;
        }

        // Merge outputs into shared artifacts
        foreach (var (k, v) in result.Outputs)
            context.Artifacts[k] = v;

        // Human approval gate — runs AFTER Execute so the human reviews real output
        if (stage.RequiresHumanApproval)
        {
            var approved = await WaitForApprovalAsync(context, stage.Name, ct);
            if (!approved)
            {
                await log.AppendAsync(AuditEventType.SafeStop, stage.Name,
                    "Human rejected or approval timed out — safe-stopping pipeline.");
                return StageResult.Fail("Approval not granted");
            }

            context.Lineage.Add(new DecisionRecord
            {
                Stage = stage.Name,
                Decision = "Approved",
                Rationale = "Human approved via POST /pipeline/{id}/approve"
            });
        }

        var exit = await stage.ExitGate(context, result);
        if (!exit.Passed)
        {
            await log.AppendAsync(AuditEventType.StageFailed, stage.Name,
                $"Exit gate failed: {exit.Reason}");
            return StageResult.Fail(exit.Reason!);
        }

        await log.AppendAsync(AuditEventType.StageCompleted, stage.Name,
            $"{stage.Name} completed and exit gate passed");
        return result;
    }

    // ── Retry wrapper: max 3 attempts, exponential backoff (2s, 4s, 8s) ──
    private async Task<StageResult> RunStageWithRetryAsync(PipelineContext context, IPipelineStage stage,
        CancellationToken ct)
    {
        StageResult? last = null;
        for (int attempt = 1; attempt <= _retry.MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var delay = _retry.GetDelay(attempt - 1);
                await context.AuditLog.AppendAsync(AuditEventType.RetryAttempt, stage.Name,
                    $"Retry {attempt}/{_retry.MaxAttempts} — waiting {delay.TotalSeconds}s");
                await Task.Delay(delay, ct);
            }

            last = await RunStageAsync(context, stage, ct);
            if (last.Succeeded) return last;

            if (attempt < _retry.MaxAttempts)
                await context.AuditLog.AppendAsync(AuditEventType.RetryScheduled, stage.Name,
                    $"Attempt {attempt} failed; scheduling retry. Reason: {last.FailureReason}");
        }

        return last ?? StageResult.Fail("No attempts made");
    }

    // ── Human approval: TCS + Task.WhenAny timeout → safe-stop if no decision arrives ──
    private async Task<bool> WaitForApprovalAsync(PipelineContext context, string stageName,
        CancellationToken ct)
    {
        var tcs = approvalService.Register(context.RunId, stageName);

        await context.AuditLog.AppendAsync(AuditEventType.ApprovalPending, stageName,
            $"Awaiting human approval. POST /pipeline/{context.RunId}/approve (or /reject). " +
            $"Timeout in {_approvalTimeout.TotalMinutes:F0} min.",
            new Dictionary<string, string>
            {
                ["run_id"] = context.RunId.ToString(),
                ["stage"] = stageName,
                ["timeout_minutes"] = _approvalTimeout.TotalMinutes.ToString("F0")
            });

        var winner = await Task.WhenAny(tcs.Task, Task.Delay(_approvalTimeout, ct));
        approvalService.Remove(context.RunId);

        if (winner != tcs.Task)
        {
            await context.AuditLog.AppendAsync(AuditEventType.ApprovalTimeout, stageName,
                $"No decision received within {_approvalTimeout.TotalMinutes:F0} min — safe-stop triggered.");
            return false;
        }

        bool approved = await tcs.Task;
        await context.AuditLog.AppendAsync(AuditEventType.ApprovalDecision, stageName,
            $"Human decision recorded: {(approved ? "APPROVED" : "REJECTED")}",
            new Dictionary<string, string> { ["decision"] = approved ? "approved" : "rejected" });

        return approved;
    }

    private static async Task<PipelineRunResult> SafeStop(IAuditLog log, string reason)
    {
        await log.AppendAsync(AuditEventType.SafeStop, "Pipeline",
            $"Pipeline safe-stopped: {reason}");
        return PipelineRunResult.SafeStopped(reason);
    }
}
