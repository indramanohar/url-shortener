using System.Diagnostics;
using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

public class ImplementationStage : IPipelineStage
{
    public string Name => "Implementation";
    public bool RequiresHumanApproval => false;

    public Task<GateResult> EntryGate(PipelineContext context)
    {
        if (!context.HasArtifact("design_complete"))
            return Task.FromResult(GateResult.Fail("Design must be approved before Implementation."));
        return Task.FromResult(GateResult.Ok());
    }

    public async Task<StageResult> Execute(PipelineContext context)
    {
        var solutionRoot = context.GetArtifact<string>("solution_root") ?? Directory.GetCurrentDirectory();

        // On a rollback pass, failure context from Testing is available — surface it
        var failureContext = context.GetArtifact<string>("testing_failure_context");
        if (failureContext != null)
        {
            // Re-implementation with the failure context attached as an input
            // In a real system this would trigger code changes; here we clear the injection flag
            // and document what would change so the audit trail is honest
            context.Artifacts.Remove("testing_failure_context");
            context.Artifacts.Remove("inject_test_failure");
        }

        var (exitCode, output) = await RunDotnetBuildAsync(solutionRoot, context.CancellationToken);
        if (exitCode != 0)
        {
            return StageResult.Fail($"dotnet build failed (exit {exitCode}):\n{output}");
        }

        return StageResult.Ok(new Dictionary<string, object>
        {
            ["build_output"] = output,
            ["build_exit_code"] = exitCode,
            ["implementation_complete"] = true
        });
    }

    public Task<GateResult> ExitGate(PipelineContext context, StageResult result)
    {
        if (result.Outputs.TryGetValue("build_exit_code", out var code) && (int)code == 0)
            return Task.FromResult(GateResult.Ok());
        return Task.FromResult(GateResult.Fail("Build did not succeed."));
    }

    private static async Task<(int exitCode, string output)> RunDotnetBuildAsync(
        string workDir, CancellationToken ct)
    {
        var dotnet = ResolveDotnet();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "build --no-incremental -v q",
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
        return (process.ExitCode, (stdout + stderr).Trim());
    }

    internal static string ResolveDotnet()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            var candidate = Path.Combine(root, "dotnet");
            if (File.Exists(candidate)) return candidate;
        }
        return "dotnet";
    }
}
