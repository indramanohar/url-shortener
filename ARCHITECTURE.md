# Architecture — Agentic SDLC Orchestration Engine

## Pipeline graph
```
Requirements -> Design (human approval) -> Implementation
                                                |
                              +-----------------+-----------------+
                              |                                   |
                           Testing                          Documentation
                              |                                   |
                              +-----------------+-----------------+
                                                |
                                     Release (human approval)
```
Dashed edge (not shown above): Testing --failure, 3x retry exhausted--> rollback to
Implementation with failure context attached. This is the "non-linear, stateful execution"
requirement — a real back-edge, not just a forward DAG.

## Stage contracts

| Stage | Entry gate | Exit gate | Human approval |
|---|---|---|---|
| Requirements | Raw ask exists | Normalized spec + ambiguities documented | No |
| Design | Normalized spec approved | API contracts, data model, stack decision recorded | **Yes** |
| Implementation | Design approved | Build succeeds, matches API contract | No (automated) |
| Testing | Implementation complete | Coverage threshold met, tests green | No (automated) |
| Documentation | Implementation complete | README + API docs + architecture overview present | No (automated) |
| Release | Testing AND Documentation both passed (sync point) | Release checklist complete | **Yes** |

Design and Release are the human gates because they're the expensive-to-undo points —
Design locks in structure everything downstream depends on; Release is irreversible once
shipped. Testing/Documentation failures are cheap to retry automatically. That asymmetry is
what "controlled autonomy" means in practice — be ready to say this out loud.

## Retry / fallback / rollback / safe-stop
- Each automated exit gate: max 3 retries, exponential backoff (2s, 4s, 8s).
- Testing fails after 3 retries -> **rollback**: re-queue Implementation with the failure
  output attached as context (the dashed edge above).
- A human-approval stage rejected twice -> **safe-stop**: halt entirely, don't auto-retry an
  explicit human "no."
- Global kill switch: any stage can be paused mid-execution; state persists in
  `PipelineContext` so it resumes rather than restarts.

## Dynamic re-planning
If Implementation discovers something that contradicts an already-approved Design decision
(e.g., chosen collision strategy conflicts with chosen short-code length), the orchestrator
inserts a back-edge to Design for re-approval with the conflict attached as context, then
resumes forward. Don't silently proceed and don't silently fail — this is the behavior that
most differentiates this from a fixed linear task chain.

## Policy guardrails (checked at specific gates, not just described in prose)
1. Dependency allow-list check before Implementation exit — no unapproved NuGet packages.
2. Secret-scanning gate before Release — no hardcoded keys/connection strings.
3. Change-control rule: Implementation output contradicting an approved Design decision
   auto-triggers Design re-approval (this *is* the dynamic re-planning rule above, as policy).

STRETCH (add only if MUST items are done): dependency vulnerability scan before Release.

## Reliability metrics — all derived from the audit log, no separate tracking system
- **Success rate** = stages passing exit gate on first attempt / total stage runs
- **Retry/rollback frequency** = retries / total attempts; rollbacks / total stage transitions
- **MTTR** = avg time between a failed gate and the next successful gate for that stage
- **End-to-end latency** = Release timestamp − Requirements start timestamp

## Audit log
Port the hash-chain pattern directly — each entry embeds the SHA-256 hash of the previous
entry, so tampering with a past entry breaks the chain (`verify_chain()`-equivalent). Append-
only. Log every stage transition, retry, rollback, and human decision. This is the same
pattern already prototyped and tested in Python earlier in this project's design phase —
translate the logic, don't redesign it from scratch.

## Limitations — documented trade-offs, not silent gaps

- **Human-approval service is single-instance and in-memory.** The `TaskCompletionSource`
  held in `PipelineApprovalService` does not survive a process restart and cannot be shared
  across multiple API instances. In a production system this would be a durable queue entry
  (e.g., an Approval record in the DB polled or pushed via SignalR). Accepted under the
  24-hour constraint.
- **In-memory cache instead of Redis.** `IMemoryCache` is node-local and evicts on restart.
  Production swap = replace with `IDistributedCache` + StackExchange.Redis, no logic change.
- **SQLite instead of SQL Server.** Connection string + provider package swap only.
- **Approval timeout triggers safe-stop.** If no human decision arrives within the configured
  window (default 10 minutes), the orchestrator halts the pipeline and logs a timeout event
  rather than proceeding. This prevents indefinite blocking but means the pipeline must be
  re-run from scratch — a restart-from-checkpoint model is the production answer.

## Interface shape (contract for Claude Code to implement against)
```csharp
public interface IPipelineStage
{
    string Name { get; }
    bool RequiresHumanApproval { get; }
    Task<GateResult> EntryGate(PipelineContext context);
    Task<StageResult> Execute(PipelineContext context);
    Task<GateResult> ExitGate(PipelineContext context, StageResult result);
}

public class PipelineContext
{
    public Dictionary<string, object> Artifacts { get; } = new();   // cross-stage outputs
    public List<DecisionRecord> Lineage { get; } = new();           // decision lineage
    public IAuditLog AuditLog { get; init; }                        // hash-chained, append-only
}

public class RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(2);
}
```

Orchestrator loop: entry gate -> execute -> exit gate -> on failure, retry up to policy limit
-> on exhaustion, rollback with context or safe-stop -> if human-approval required, pause and
wait -> record every transition to the audit log -> compute metrics from those timestamps.
Testing and Documentation run via `Task.WhenAll` once Implementation's exit gate passes —
that's the real parallel branch, not a simulated one.
