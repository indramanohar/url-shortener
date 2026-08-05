# Tasks — 24-Hour Build, Reprioritized

Original scope assumed 2-3 days. This is the compressed version. MUST items total ~14-15 hrs —
that leaves a buffer inside 24 hours for setbacks. If you fall behind, cut STRETCH items first,
never a MUST item. Commit after each checked item — the commit history is itself evidence of
process, which matters for this submission.

## MUST HAVE

- [ ] **Setup** (~30 min): repo init, solution scaffold, `dotnet new webapi`, confirm
      `dotnet build` runs clean before writing any feature code.
- [ ] **Core URL shortener API** (~2.5 hrs): `POST /shorten`, `GET /{code}` (302 redirect),
      `DELETE /{code}`. EF Core model + migration. This is the Greenfield scenario — run it
      through the orchestration engine once that exists, don't hand-build it outside the engine.
- [ ] **Orchestration engine core** (~3 hrs): `IPipelineStage`, `PipelineContext`, graph
      executor walking Requirements -> Design -> Implementation sequentially. Get this
      genuinely working end-to-end before adding parallel branches or retry logic.
- [ ] **Parallel branch** (~1.5 hrs): Testing + Documentation via `Task.WhenAll`, syncing
      before Release.
- [ ] **One real retry -> rollback** (~1 hr): force a Testing failure path, confirm it
      rolls back to Implementation with failure context attached, visible in the audit log.
- [ ] **One real human-approval pause -> resume** (~1 hr): Design and/or Release gate actually
      pauses execution and waits — not a stubbed always-true check.
- [ ] **Audit log** (~1.5 hrs): port the hash-chain append-only pattern; log every transition,
      retry, rollback, approval decision. Compute the four metrics from it.
- [ ] **Run all 3 scenarios through the engine** (~2 hrs): Greenfield (core API), Brownfield
      (add analytics endpoint to the existing service), Ambiguous (implement the reliability
      resolution from `PROJECT_BRIEF.md`). Capture the audit log output from each run — that's
      your evidence artifact for the submission.
- [ ] **Tests** (~2 hrs): unit tests for stage gate logic + retry/rollback behavior;
      integration tests for the shorten/redirect/analytics endpoints.
- [ ] **Docs** (~2 hrs, much of this is already drafted in `PROJECT_BRIEF.md` and
      `ARCHITECTURE.md` — assemble, don't rewrite from scratch):
  - Architecture overview (components, orchestration model, control flow, key decisions)
  - Setup instructions
  - Testing approach, limitations, trade-offs (Redis-vs-in-memory, scale target, etc.)
  - Final Engineering Summary: plan/rationale, artifacts, risks/trade-offs, assumptions,
    limitations — this can lean heavily on `PROJECT_BRIEF.md`'s ambiguity list almost verbatim.

## STRETCH (cut first if behind)

- [ ] Redis instead of in-memory cache
- [ ] Dependency vulnerability scan gate before Release
- [ ] Analytics breakdown by referrer/device
- [ ] Rate limiting middleware (note as a documented gap if cut, don't silently drop it)
- [ ] Any UI/dashboard for metrics — a log line or a single `GET /metrics` endpoint is enough

## Explicitly out of scope — say so in the Final Engineering Summary, don't leave it implicit
- Geo-based analytics
- Multi-region/HA deployment
- Real Twitter-scale throughput (stated scale target is low-thousands/day — say what would
  change at real scale: sharded ID generation, geo-distributed cache)
