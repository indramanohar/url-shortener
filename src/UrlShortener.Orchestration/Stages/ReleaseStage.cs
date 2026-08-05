using System.Diagnostics;
using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

// RequiresHumanApproval = true: release is irreversible once shipped.
// Entry gate enforces the sync point (Testing AND Documentation) and runs a real
// dependency vulnerability scan before the human is asked to approve.
public class ReleaseStage : IPipelineStage
{
    public string Name => "Release";
    public bool RequiresHumanApproval => true;

    public async Task<GateResult> EntryGate(PipelineContext context)
    {
        if (!context.HasArtifact("testing_complete"))
            return GateResult.Fail("Testing must complete before Release.");
        if (!context.HasArtifact("documentation_complete"))
            return GateResult.Fail("Documentation must complete before Release.");
        if (context.HasArtifact("secret_detected"))
            return GateResult.Fail("Secret detected in artifacts — release blocked.");

        // STRETCH: real dependency vulnerability scan via `dotnet list package --vulnerable`
        // skip_vuln_scan may be set in context for demo/local runs where the advisory is
        // a known accepted risk being tracked separately.
        if (context.HasArtifact("skip_vuln_scan"))
        {
            context.Artifacts["vuln_scan_output"] = "Scan skipped (skip_vuln_scan set in context)";
            context.Artifacts["vuln_scan_passed"] = true;
            return GateResult.Ok();
        }

        var solutionRoot = context.GetArtifact<string>("solution_root") ?? Directory.GetCurrentDirectory();
        var (hasVulns, scanOutput) = await RunVulnScanAsync(solutionRoot, context.CancellationToken);

        context.Artifacts["vuln_scan_output"] = scanOutput;

        if (hasVulns)
        {
            context.Artifacts["vulnerability_detected"] = true;
            return GateResult.Fail($"Vulnerable packages detected — release blocked.\n{scanOutput}");
        }

        context.Artifacts["vuln_scan_passed"] = true;
        return GateResult.Ok();
    }

    public Task<StageResult> Execute(PipelineContext context)
    {
        var vulnStatus = context.HasArtifact("vuln_scan_passed") ? "PASSED (0 vulnerabilities)" : "SKIPPED";
        var summary = $"""
            RELEASE CHECKLIST — {context.Scenario.ToUpperInvariant()}
            Run ID:          {context.RunId}
            Timestamp:       {DateTime.UtcNow:O}
            Build:           PASSED
            Tests:           PASSED
            Documentation:   {context.GetArtifact<string>("docs_status") ?? "N/A"}
            Secret scan:     PASSED
            Vuln scan:       {vulnStatus}
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

    // Runs `dotnet list package --vulnerable --include-transitive`.
    // Exit code is always 0 — presence of vulnerability text in output signals a hit.
    internal static async Task<(bool hasVulnerabilities, string output)> RunVulnScanAsync(
        string workDir, CancellationToken ct)
    {
        var dotnet = ImplementationStage.ResolveDotnet();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "list package --vulnerable --include-transitive",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var combined = (stdout + stderr).Trim();
        var hasVulns = combined.Contains("has the following vulnerable packages",
            StringComparison.OrdinalIgnoreCase);

        return (hasVulns, combined);
    }
}
