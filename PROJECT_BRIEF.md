# Project Brief — Normalized Requirements

Source spec deliberately left "core APIs, analytics, and reliability features" undefined.
This document is the normalization artifact — cite it directly in the Final Engineering
Summary's "Requirement Understanding" section.

## Ambiguities identified in the source spec
1. Scope of "core APIs" — shorten + redirect only, or also aliases/deactivation/ownership?
2. Auth model — anonymous, or per-user/API-key ownership of links?
3. Analytics depth — raw count, or referrer/device/time breakdown?
4. "Reliability features" — completely undefined term.
5. No throughput/latency SLA given.
6. Redirect semantics — 301 vs 302?
7. Short-code collision strategy — unspecified.
8. Link expiration/TTL — unspecified.

## Resolutions (state these explicitly in the summary — don't leave them implicit)
- **Redirect: 302, not 301.** 301 gets cached by the browser, which silently breaks click
  analytics on repeat visits. This is a deliberate, defensible choice — say so if asked.
- **Auth**: API key required to create/manage links; redirect path stays fully anonymous
  (that's the product — a public shortener can't gate redirects behind auth).
- **Reliability, normalized to**: rate limiting per API key/IP, cache in front of the redirect
  hot path (in-memory for this build, Redis in production — documented trade-off), idempotent
  creation (same long URL + same alias twice doesn't duplicate), fallback to direct DB read if
  cache is unavailable, a health-check endpoint.
- **Scale target, stated explicitly rather than left unstated**: low-thousands of creates/day,
  tens-of-thousands of redirects/day. Note in limitations what changes at real scale
  (sharded ID generation, geo-distributed cache).

## Functional spec

| Endpoint | Behavior |
|---|---|
| `POST /shorten` | Long URL → short code. Optional custom alias, optional TTL. |
| `GET /{code}` | 302 redirect to original URL; records a click event. |
| `DELETE /{code}` | Deactivate a link. |
| `GET /{code}/analytics` | Click count + timestamp series. (Geo/device explicitly out of scope — documented limitation, not a silent gap.) |
| `GET /health` | Liveness/readiness for the reliability story. |

## Mapping to the three required scenarios (build once, run three times — don't triple the work)
- **Greenfield** = the core shorten/redirect API, built from scratch via the orchestration engine.
- **Brownfield** = adding the analytics endpoint + click-tracking to the already-existing service
  — a genuine enhancement touching existing modules, run through the engine a second time.
- **Ambiguous** = the reliability features, since that's the one term the source spec never
  defined. This scenario's "Requirements" stage output *is* the resolution written above —
  show the orchestrator producing that resolution, not just implementing a canned answer.
