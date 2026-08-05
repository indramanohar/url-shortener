# URL Shortener — Agentic SDLC Orchestration

A URL shortener built *by* an orchestration engine that executes the full SDLC as a
stateful, auditable pipeline. The product (shorten/redirect/analytics API) and the
process that built it (Requirements → Design → Implementation → Testing ‖ Documentation
→ Release) are both first-class deliverables.

---

## Quick start

```bash
# Prerequisites: .NET 8 SDK
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"   # Homebrew install path
export PATH="$DOTNET_ROOT:$PATH"

dotnet build
dotnet test
dotnet run --project src/UrlShortener.Api
# API available at http://localhost:5000
```

---

## URL Shortener API

All write endpoints require `X-Api-Key: <your-key>` header. Redirects are anonymous.

| Endpoint | Auth | Description |
|---|---|---|
| `POST /shorten` | Required | Create a short link. Optional `alias` and `ttlDays`. |
| `GET /{code}` | None | 302 redirect to original URL. Records a click. |
| `DELETE /{code}` | Required | Deactivate a link (soft delete, key-scoped). |
| `GET /{code}/analytics` | None | Click count + timestamp series. |
| `GET /health` | None | Liveness / DB reachability check. |
| `GET /pipeline/{id}/metrics` | None | Four reliability metrics for a single run. |
| `GET /pipeline/metrics` | None | Aggregate metrics across all runs. |

**POST /shorten — example:**
```json
POST /shorten
X-Api-Key: my-key
{ "url": "https://example.com/very/long/path", "alias": "ex", "ttlDays": 30 }

→ 201 { "code": "ex", "shortUrl": "http://localhost:5000/ex", ... }
```

**Key decisions documented, not left implicit:**
- **302, not 301** — 301 is cached by browsers; repeat visits would skip click recording.
- **Idempotent creation** — same URL + same alias returns the existing record, no duplicate.
- **Soft delete** — `IsActive=false`; the row is retained so audit trails are preserved.
- **Rate limiting** — write operations (POST/DELETE) are throttled per API key; redirects are throttled per client IP. Both limits are config-driven (`appsettings.json`) and return `429 + Retry-After` on breach.

---

## Orchestration Engine API

All mutating pipeline endpoints (`/run`, `/approve`, `/reject`, `/cancel`) require `X-Api-Key: <your-key>`. Read endpoints are unauthenticated.

Run any of the three SDLC scenarios through the full pipeline:

| Endpoint | Description |
|---|---|
| `POST /pipeline/run` | Start a pipeline run. Body: `{ "scenario": "greenfield" }` |
| `POST /pipeline/{id}/approve` | Approve a human-gate (Design or Release). |
| `POST /pipeline/{id}/reject` | Reject a human-gate → safe-stop. |
| `POST /pipeline/{id}/cancel` | Cancel a running pipeline mid-execution → `Cancelled` status. |
| `GET /pipeline/{id}` | Status, current stage, abbreviated audit log. |
| `GET /pipeline/{id}/audit` | Full audit log + chain integrity verification. |
| `GET /pipeline` | List all pipeline runs. |

**Scenario values:** `greenfield` · `brownfield` · `ambiguous`

**Start with failure injection (demonstrates retry→rollback):**
```json
POST /pipeline/run
{ "scenario": "greenfield", "injectTestFailure": true }
```

**Skip vulnerability scan (accepted-risk bypass):**
```json
POST /pipeline/run
{ "scenario": "greenfield", "skipVulnScan": true }
```
By default the Release entry gate runs `dotnet list package --vulnerable`. A detected CVE blocks release; `skipVulnScan: true` records the bypass decision in the audit log and proceeds.

**Human approval flow:**
1. `POST /pipeline/run` → returns `{ "runId": "...", "approveUrl": "/pipeline/{id}/approve" }`
2. Pipeline pauses at Design stage — poll `GET /pipeline/{id}` until `"pendingApproval": true`
3. `POST /pipeline/{id}/approve` → pipeline resumes
4. Pipeline pauses again at Release gate — repeat step 3
5. `GET /pipeline/{id}/audit` → verify chain integrity = `"VALID"`

---

## Architecture

### Pipeline graph

```
Requirements → Design (human ✓) → Implementation
                                        │
                         ┌──────────────┴──────────────┐
                         │                             │
                      Testing                   Documentation
                    (3× retry)                        │
                         │                             │
                         └──────────────┬──────────────┘
                                        │ (sync point)
                                   Release (human ✓)
```

Testing failure back-edge: after 3 retries (2 s / 4 s / 8 s), rolls back to
Implementation with failure context attached, then re-runs Testing.

### Components

| Component | Role |
|---|---|
| `HashChainedAuditLog` | SHA-256 hash-chain, append-only. Every transition, retry, rollback, and human decision is logged. `VerifyChain()` detects tampering. |
| `PipelineOrchestrator` | Main loop: entry gate → execute → [human approval] → exit gate → retry / rollback / safe-stop. |
| `PipelineApprovalService` | Singleton holding `TaskCompletionSource<bool>` per pending run. API endpoint calls `Resolve()` to unblock the orchestrator. |
| `IPipelineStage` | Interface from `ARCHITECTURE.md`. Six implementations: Requirements, Design, Implementation, Testing, Documentation, Release. |
| `MetricsCalculator` | Derives four reliability metrics from the audit log: stage success rate, retry frequency, MTTR, and end-to-end latency. |
| `UrlShortenerService` | `IMemoryCache` in front of EF Core / SQLite; cache-miss fallback to DB. |

### Human-approval mechanism

```csharp
var tcs = approvalService.Register(runId, stageName);
// Log "pending" immediately
var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
approvalService.Remove(runId);
// If timeout won → safe-stop (logged)
// If tcs won → log approved/rejected
```

Timeout default: 10 minutes. Configurable via `PipelineOrchestrator` constructor.

---

## Three scenarios

| Scenario | What the orchestrator builds |
|---|---|
| **Greenfield** | Core shorten / redirect / delete API from scratch. |
| **Brownfield** | Analytics endpoint + click-tracking added to existing service. |
| **Ambiguous** | Reliability features — the Requirements stage *produces* the resolution of the undefined term, demonstrating how the engine handles spec gaps. |

---

## Testing approach

```
tests/
  UrlShortener.Orchestration.Tests/   # 42 unit tests
    AuditLogTests.cs          — hash chain correctness, tamper detection
    RetryPolicyTests.cs       — exponential backoff timing
    StageGateTests.cs         — entry/exit gate logic for all 6 stages
    ApprovalServiceTests.cs   — TCS resolve, timeout, HasPending lifecycle
    MetricsCalculatorTests.cs — success rate, retry frequency, MTTR, latency

  UrlShortener.Api.Tests/             # 18 integration tests
    UrlEndpointsTests.cs   — shorten/redirect/delete/analytics/health via
                             WebApplicationFactory + in-memory SQLite
    RateLimitTests.cs      — 429 on breach, Retry-After header, independent
                             key buckets, redirect throttle, health bypass
```

Run: `dotnet test` (60 tests, all green)

---

## Trade-offs and limitations

| Trade-off | Decision | Production path |
|---|---|---|
| Database | SQLite | Change provider + connection string to SQL Server / Postgres |
| Cache | `IMemoryCache` (node-local) | `IDistributedCache` + Redis; no logic changes required |
| Redirect | 302 | Intentional — 301 breaks repeat-visit analytics |
| Approval persistence | In-memory TCS singleton | Durable approval record in DB + SignalR or polling |
| Approval timeout | Safe-stop; pipeline must restart | Checkpoint resume from last completed stage |
| Scale | Low-thousands creates/day | Sharded ID generation, geo-distributed cache at real scale |
| Rate limiter window | Fixed window (simpler) | Sliding window avoids burst spikes at window boundaries |

**Explicitly out of scope** (documented, not silently dropped):
- Geo / device analytics breakdown
- Multi-region / HA deployment
- Real Twitter-scale throughput

---

## Final Engineering Summary

### Requirement understanding

Source spec left "core APIs, analytics, and reliability features" undefined.
`PROJECT_BRIEF.md` is the normalization artifact — eight ambiguities identified and
resolved explicitly (redirect semantics, auth model, analytics depth, collision strategy,
idempotency, TTL, health check, scale target). These resolutions are not hidden in code —
they appear as the output of the Requirements stage in every pipeline run.

### Artifacts

- `ARCHITECTURE.md` — pipeline graph, stage contracts, retry/rollback/safe-stop policy,
  audit log spec, limitations.
- `PROJECT_BRIEF.md` — ambiguity list + resolutions.
- `TASKS.md` — time-boxed MUST/STRETCH checklist; git history maps to it commit by commit.
- Audit log output from each scenario run (accessible via `GET /pipeline/{id}/audit`).

### Key risks and mitigations

| Risk | Mitigation |
|---|---|
| Approval TCS lost on restart | Documented trade-off; production = durable record |
| Test SQLite files accumulate | Factory uses per-run GUID path; cleanup on CI |
| inject_test_failure in prod | Flag only activates when explicitly set in artifacts; no default |

### Assumptions

- Single-instance deployment for this build.
- API key is opaque — no key management, rotation, or revocation in scope.
- Analytics are eventually consistent (click recorded after redirect returns).
