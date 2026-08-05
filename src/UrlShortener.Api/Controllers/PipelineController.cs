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
            AuditLog = auditLog
        };

        // Solution root so stages can invoke dotnet build / dotnet test
        context.Artifacts["solution_root"] = Directory.GetCurrentDirectory();

        // Deliberate failure injection for retry→rollback demonstration
        if (request.InjectTestFailure)
            context.Artifacts["inject_test_failure"] = true;

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
            statusUrl = $"/pipeline/{run.Id}"
        });
    }

    [HttpPost("{id}/approve")]
    public IActionResult Approve(Guid id)
    {
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
        var run = runStore.Get(id);
        if (run == null) return NotFound();

        if (!approvalService.HasPending(id))
            return Conflict("No approval is currently pending for this run.");

        approvalService.Resolve(id, approved: false);
        return Ok(new { runId = id, decision = "rejected" });
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
}

public record StartPipelineRequest(string Scenario, bool InjectTestFailure = false);
