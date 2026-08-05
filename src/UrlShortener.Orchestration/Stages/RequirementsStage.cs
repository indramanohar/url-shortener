using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

public class RequirementsStage : IPipelineStage
{
    public string Name => "Requirements";
    public bool RequiresHumanApproval => false;

    public Task<GateResult> EntryGate(PipelineContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Scenario))
            return Task.FromResult(GateResult.Fail("No scenario description provided."));
        return Task.FromResult(GateResult.Ok());
    }

    public Task<StageResult> Execute(PipelineContext context)
    {
        // Produce a normalized spec from the raw scenario description.
        // Ambiguities are explicitly identified and resolved — this output IS the resolution artifact.
        var spec = context.Scenario switch
        {
            "greenfield" => NormalizeGreenfield(),
            "brownfield" => NormalizeBrownfield(),
            "ambiguous" => NormalizeAmbiguous(),
            _ => $"Custom scenario: {context.Scenario}"
        };

        return Task.FromResult(StageResult.Ok(new Dictionary<string, object>
        {
            ["normalized_spec"] = spec,
            ["ambiguities_identified"] = GetAmbiguities(context.Scenario),
            ["requirements_complete"] = true
        }));
    }

    public Task<GateResult> ExitGate(PipelineContext context, StageResult result)
    {
        if (!result.Outputs.ContainsKey("normalized_spec"))
            return Task.FromResult(GateResult.Fail("normalized_spec artifact missing."));
        if (!result.Outputs.ContainsKey("ambiguities_identified"))
            return Task.FromResult(GateResult.Fail("ambiguities_identified artifact missing."));
        return Task.FromResult(GateResult.Ok());
    }

    private static string NormalizeGreenfield() =>
        """
        GREENFIELD: Core URL Shortener API

        Scope: POST /shorten, GET /{code} (redirect), DELETE /{code}
        Resolutions:
          - Redirect = 302 (not 301 — 301 breaks analytics on repeat visits)
          - Auth = API key header on writes; redirects are anonymous
          - Collision strategy = random Base62(7), retry up to 10x
          - TTL = optional, stored as ExpiresAt
          - Idempotent creation: same URL + same alias returns existing record
        Out of scope: analytics (Brownfield), reliability (Ambiguous)
        """;

    private static string NormalizeBrownfield() =>
        """
        BROWNFIELD: Add Analytics to Existing Service

        Scope: GET /{code}/analytics + click-tracking on redirect
        Enhancement touches: RedirectController (fire click record), new analytics endpoint
        Ambiguity resolved: analytics = total click count + timestamp series only
          (geo/device explicitly out of scope — documented limitation, not a silent gap)
        Idempotency: analytics endpoint is read-only, no new write ambiguities
        """;

    private static string NormalizeAmbiguous() =>
        """
        AMBIGUOUS: Reliability Features

        Source spec used term "reliability features" without definition.
        Resolution produced by this Requirements stage:
          - Rate limiting per API key / IP (documented as gap if cut)
          - In-memory cache (IMemoryCache) in front of redirect hot path
            Trade-off: node-local; Redis in production — documented
          - Idempotent link creation (same long URL + alias = no duplicate)
          - Fallback to DB read if cache unavailable
          - Health check endpoint (GET /health)
        Scale target stated explicitly: low-thousands creates/day, tens-thousands redirects/day
        """;

    private static string GetAmbiguities(string scenario) =>
        scenario switch
        {
            "ambiguous" => "1. 'reliability features' undefined — resolved above\n2. No SLA given — stated scale target instead",
            "greenfield" => "1. 301 vs 302 — resolved: 302\n2. Auth model — resolved: API key\n3. Collision strategy — resolved: random Base62",
            _ => "No significant ambiguities"
        };
}
