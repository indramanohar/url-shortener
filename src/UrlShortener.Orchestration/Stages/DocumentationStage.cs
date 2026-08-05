using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

// Runs in parallel with TestingStage via Task.WhenAll after Implementation completes.
public class DocumentationStage : IPipelineStage
{
    public string Name => "Documentation";
    public bool RequiresHumanApproval => false;

    public Task<GateResult> EntryGate(PipelineContext context)
    {
        if (!context.HasArtifact("implementation_complete"))
            return Task.FromResult(GateResult.Fail("Implementation must complete before Documentation."));
        return Task.FromResult(GateResult.Ok());
    }

    public async Task<StageResult> Execute(PipelineContext context)
    {
        var solutionRoot = context.GetArtifact<string>("solution_root") ?? Directory.GetCurrentDirectory();

        // Verify README exists with substantive content
        var readmePath = Path.Combine(solutionRoot, "README.md");
        string docsStatus;

        if (!File.Exists(readmePath))
        {
            docsStatus = "README.md missing — will be created at Release";
        }
        else
        {
            var content = await File.ReadAllTextAsync(readmePath, context.CancellationToken);
            docsStatus = content.Length > 200
                ? $"README.md present ({content.Length} chars)"
                : "README.md exists but is sparse";
        }

        // Verify ARCHITECTURE.md is present (already part of repo)
        var archPath = Path.Combine(solutionRoot, "ARCHITECTURE.md");
        var archStatus = File.Exists(archPath) ? "ARCHITECTURE.md present" : "ARCHITECTURE.md missing";

        return StageResult.Ok(new Dictionary<string, object>
        {
            ["docs_status"] = docsStatus,
            ["arch_status"] = archStatus,
            ["documentation_complete"] = true
        });
    }

    public Task<GateResult> ExitGate(PipelineContext context, StageResult result)
    {
        if (!result.Outputs.ContainsKey("documentation_complete"))
            return Task.FromResult(GateResult.Fail("documentation_complete artifact missing."));
        return Task.FromResult(GateResult.Ok());
    }
}
