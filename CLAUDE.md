# Project: URL Shortener via Agentic SDLC Orchestration
Schwab take-home — 24-hour build. Read `PROJECT_BRIEF.md`, `ARCHITECTURE.md`, and `TASKS.md`
in this order before writing code. This file is the standing brief — keep it loaded, refer to
the other three for depth.

## What we're building
Two things, not one:
1. A URL shortener (shorten/redirect/delete/analytics) — the *product*.
2. An agentic orchestration engine that runs the SDLC (Requirements → Design → Implementation →
   Testing + Documentation in parallel → Release) to *build* that product — the *differentiator*.
   Full graph and gate contracts are in `ARCHITECTURE.md`.

## Stack
.NET 8, ASP.NET Core Web API, EF Core + SQL Server (or SQLite for speed if time is short —
document the swap as a trade-off, don't silently do it), xUnit for tests.
In-memory cache (`IMemoryCache`) standing in for Redis — document this as a deliberate
trade-off under the 24-hour constraint, not an oversight.

## Non-negotiables (do not skip these under time pressure)
- The orchestration engine must actually execute — no stubbed/fake stage transitions.
- At least one real retry→rollback happens and is visible in the audit log.
- At least one real human-approval pause→resume happens (Design and/or Release gate).
- Every stage transition, retry, rollback, and approval writes to the audit log
  (`ARCHITECTURE.md` has the hash-chain pattern already proven in an earlier prototype —
  port the logic, don't redesign it).
- Commit after every completed subtask — the git history itself is part of what's being
  evaluated (real process, not just a final drop).

## Priority order — work top to bottom, cut from the bottom if time runs out
See `TASKS.md` for the full time-boxed checklist. If behind schedule, cut STRETCH items first;
never cut a MUST item to add polish.

## Build/test commands
- `dotnet build`
- `dotnet test`
- `dotnet run --project src/UrlShortener.Api`

## Working agreement for this session
- Confirm the plan for a stage before implementing it if anything in `ARCHITECTURE.md` seems
  ambiguous — ask, don't guess silently, same principle the assignment itself is testing.
- After ~30 min of work, commit. If a session runs long, `/clear` and this file reloads
  automatically — don't try to hold everything in one context window.
