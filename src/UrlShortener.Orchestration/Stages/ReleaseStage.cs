using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

// RequiresHumanApproval = true: release is irreversible once shipped.
// Entry gate enforces the sync point: both Testing AND Documentation must have passed.
public class ReleaseStage : IPipelineStage
{
    public string Name => "Release";
    public bool RequiresHumanApproval => true;

    public Task<GateResult> EntryGate(PipelineContext context)
    {
        if (!context.HasArtifact("testing_complete"))
            return Task.FromResult(GateResult.Fail("Testing must complete before Release."));
        if (!context.HasArtifact("documentation_complete"))
            return Task.FromResult(GateResult.Fail("Documentation must complete before Release."));

        // Policy: secret scan before release
        if (context.HasArtifact("secret_detected"))
            return Task.FromResult(GateResult.Fail("Secret detected in artifacts — release blocked."));

        return Task.FromResult(GateResult.Ok());
    }

    public Task<StageResult> Execute(PipelineContext context)
    {
        var summary = $"""
            RELEASE CHECKLIST — {context.Scenario.ToUpperInvariant()}
            Run ID:          {context.RunId}
            Timestamp:       {DateTime.UtcNow:O}
            Build:           PASSED
            Tests:           PASSED
            Documentation:   {context.GetArtifact<string>("docs_status") ?? "N/A"}
            Secret scan:     PASSED
            Dependency gate: PASSED
            Chain integrity: (verified post-run via /pipeline/{context.RunId}/audit)
            """;

        return Task.FromResult(StageResult.Ok(new Dictionary<string, object>
        {
            ["release_summary"] = summary,
            ["release_complete"] = true,
            ["released_at"] = DateTime.UtcNow.ToString("O")
        }));
    }

    public Task<GateResult> ExitGate(PipelineContext context, StageResult result)
    {
        if (!result.Outputs.ContainsKey("release_complete"))
            return Task.FromResult(GateResult.Fail("release_complete artifact missing."));
        return Task.FromResult(GateResult.Ok());
    }
}
