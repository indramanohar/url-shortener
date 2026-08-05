using Microsoft.AspNetCore.Mvc;
using UrlShortener.Orchestration.AuditLog;
using UrlShortener.Orchestration.Core;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("pipeline")]
public class PipelineController(
    PipelineOrchestrator orchestrator,
    PipelineApprovalService approvalService,
    PipelineRunStore runStore) : ControllerBase
{
    [HttpPost("run")]
    public IActionResult StartRun([FromBody] StartPipelineRequest request)
    {
        if (GetApiKey() == null) return Unauthorized("X-Api-Key header is required.");

        if (string.IsNullOrWhiteSpace(request.Scenario))
            return BadRequest("scenario is required (greenfield | brownfield | ambiguous).");

        var auditLog = new HashChainedAuditLog();
        var run = new PipelineRunState
        {
            Id = Guid.NewGuid(),
            Scenario = request.Scenario,
            AuditLog = auditLog
        };
        runStore.Add(run);

        var context = new PipelineContext
        {
            RunId = run.Id,
            Scenario = request.Scenario,
            AuditLog = auditLog,
            CancellationToken = run.Cts.Token
        };

        // Solution root so stages can invoke dotnet build / dotnet test
        context.Artifacts["solution_root"] = Directory.GetCurrentDirectory();

        if (request.InjectTestFailure)
            context.Artifacts["inject_test_failure"] = true;

        if (request.SkipVulnScan)
            context.Artifacts["skip_vuln_scan"] = true;

        // Fire and forget — pipeline runs on a background thread
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await orchestrator.RunAsync(context);
                run.Status = result.Status;
                run.StoppedReason = result.StoppedReason;
            }
            catch (Exception ex)
            {
                run.Status = PipelineStatus.SafeStopped;
                run.StoppedReason = $"Unhandled exception: {ex.Message}";
            }
            finally
            {
                run.CompletedAt = DateTime.UtcNow;
            }
        });

        return Accepted(new
        {
            runId = run.Id,
            scenario = run.Scenario,
            status = run.Status.ToString(),
            approveUrl = $"/pipeline/{run.Id}/approve",
            rejectUrl = $"/pipeline/{run.Id}/reject",
            cancelUrl = $"/pipeline/{run.Id}/cancel",
            statusUrl = $"/pipeline/{run.Id}"
        });
    }

    [HttpPost("{id}/approve")]
    public IActionResult Approve(Guid id)
    {
        if (GetApiKey() == null) return Unauthorized("X-Api-Key header is required.");

        var run = runStore.Get(id);
        if (run == null) return NotFound();

        if (!approvalService.HasPending(id))
            return Conflict("No approval is currently pending for this run.");

        approvalService.Resolve(id, approved: true);
        return Ok(new { runId = id, decision = "approved" });
    }

    [HttpPost("{id}/reject")]
    public IActionResult Reject(Guid id)
    {
        if (GetApiKey() == null) return Unauthorized("X-Api-Key header is required.");

        var run = runStore.Get(id);
        if (run == null) return NotFound();

        if (!approvalService.HasPending(id))
            return Conflict("No approval is currently pending for this run.");

        approvalService.Resolve(id, approved: false);
        return Ok(new { runId = id, decision = "rejected" });
    }

    [HttpPost("{id}/cancel")]
    public IActionResult Cancel(Guid id)
    {
        if (GetApiKey() == null) return Unauthorized("X-Api-Key header is required.");

        var run = runStore.Get(id);
        if (run == null) return NotFound();

        if (run.Status != PipelineStatus.Running)
            return Conflict($"Run is not in a cancellable state (status: {run.Status}).");

        run.Cts.Cancel();
        return Ok(new { runId = id, status = "cancellation requested" });
    }

    [HttpGet("{id}")]
    public IActionResult GetStatus(Guid id)
    {
        var run = runStore.Get(id);
        if (run == null) return NotFound();

        var entries = run.AuditLog.Entries;
        return Ok(new
        {
            runId = run.Id,
            scenario = run.Scenario,
            status = run.Status.ToString(),
            stoppedReason = run.StoppedReason,
            startedAt = run.StartedAt,
            completedAt = run.CompletedAt,
            pendingApproval = approvalService.HasPending(run.Id),
            auditEntries = entries.Select(e => new
            {
                seq = e.Sequence,
                timestamp = e.Timestamp,
                eventType = e.EventType.ToString(),
                stage = e.StageName,
                message = e.Message,
                hash = e.Hash[..8] + "…"   // abbreviated for readability
            })
        });
    }

    [HttpGet("{id}/audit")]
    public IActionResult GetAudit(Guid id)
    {
        var run = runStore.Get(id);
        if (run == null) return NotFound();

        var valid = run.AuditLog.VerifyChain();
        return Ok(new
        {
            runId = run.Id,
            chainIntegrity = valid ? "VALID" : "TAMPERED",
            entries = run.AuditLog.Entries
        });
    }

    [HttpGet("{id}/metrics")]
    public IActionResult GetMetrics(Guid id)
    {
        var run = runStore.Get(id);
        if (run == null) return NotFound();

        var metrics = MetricsCalculator.Calculate(run.Id, run.Scenario, run.AuditLog.Entries);
        return Ok(FormatMetrics(metrics));
    }

    [HttpGet("metrics")]
    public IActionResult GetAggregateMetrics()
    {
        var runs = runStore.All().ToList();
        if (runs.Count == 0) return Ok(new { message = "No pipeline runs recorded yet." });

        var allMetrics = runs.Select(r =>
            MetricsCalculator.Calculate(r.Id, r.Scenario, r.AuditLog.Entries)).ToList();

        var completed = allMetrics.Where(m => m.EndToEndLatency.HasValue).ToList();

        return Ok(new
        {
            runCount               = runs.Count,
            completedCount         = runs.Count(r => r.Status == PipelineStatus.Completed),
            avgSuccessRatePct      = allMetrics.Average(m => m.SuccessRatePercent).ToString("F1"),
            avgRetryFrequency      = allMetrics.Average(m => m.RetryFrequency).ToString("F2"),
            totalRollbacks         = allMetrics.Sum(m => m.TotalRollbacks),
            avgEndToEndLatencySec  = completed.Count > 0
                ? completed.Average(m => m.EndToEndLatency!.Value.TotalSeconds).ToString("F1")
                : "N/A",
            byRun = allMetrics.Select(FormatMetrics)
        });
    }

    [HttpGet]
    public IActionResult ListRuns() =>
        Ok(runStore.All().Select(r => new
        {
            runId = r.Id,
            scenario = r.Scenario,
            status = r.Status.ToString(),
            startedAt = r.StartedAt,
            completedAt = r.CompletedAt
        }));

    private string? GetApiKey() =>
        Request.Headers.TryGetValue("X-Api-Key", out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.ToString() : null;

    private static object FormatMetrics(PipelineMetrics m) => new
    {
        runId                  = m.RunId,
        scenario               = m.Scenario,
        successRatePct         = m.SuccessRatePercent.ToString("F1"),
        retryFrequency         = m.RetryFrequency.ToString("F2"),
        totalRollbacks         = m.TotalRollbacks,
        firstAttemptSuccesses  = m.FirstAttemptSuccesses,
        totalStageRuns         = m.TotalStageRuns,
        totalRetries           = m.TotalRetries,
        mttrSeconds            = m.MeanTimeToRecovery.HasValue
            ? m.MeanTimeToRecovery.Value.TotalSeconds.ToString("F1") : "N/A",
        endToEndLatencySeconds = m.EndToEndLatency.HasValue
            ? m.EndToEndLatency.Value.TotalSeconds.ToString("F1") : "N/A"
    };
}

public record StartPipelineRequest(string Scenario, bool InjectTestFailure = false, bool SkipVulnScan = false);
