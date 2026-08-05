using System.Diagnostics;
using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

// Runs in parallel with DocumentationStage via Task.WhenAll.
// On max-retry exhaustion → rollback to ImplementationStage with failure context attached.
//
// inject_test_failure: When a pipeline run sets this artifact, Testing intentionally fails on
// that attempt even if dotnet test exits 0. Purpose: demonstrate the retry→rollback path in a
// controlled, observable way. The artifact is cleared by ImplementationStage on rollback so the
// next Testing attempt succeeds. This is documented in the audit log — not a hidden stub.
public class TestingStage : IPipelineStage
{
    public string Name => "Testing";
    public bool RequiresHumanApproval => false;

    public Task<GateResult> EntryGate(PipelineContext context)
    {
        if (!context.HasArtifact("implementation_complete"))
            return Task.FromResult(GateResult.Fail("Implementation must complete before Testing."));
        return Task.FromResult(GateResult.Ok());
    }

    public async Task<StageResult> Execute(PipelineContext context)
    {
        var solutionRoot = context.GetArtifact<string>("solution_root") ?? Directory.GetCurrentDirectory();
        var (exitCode, output) = await RunDotnetTestAsync(solutionRoot, context.CancellationToken);

        // Deliberate failure injection for retry→rollback demonstration
        if (context.HasArtifact("inject_test_failure"))
        {
            return StageResult.Fail(
                $"[INJECTED FAILURE — demonstrating retry→rollback path]\n" +
                $"dotnet test exit={exitCode}. Failure injection is active; " +
                $"ImplementationStage will clear this on rollback.\n{output}");
        }

        if (exitCode != 0)
            return StageResult.Fail($"dotnet test failed (exit {exitCode}):\n{output}");

        return StageResult.Ok(new Dictionary<string, object>
        {
            ["test_output"] = output,
            ["test_exit_code"] = exitCode,
            ["testing_complete"] = true
        });
    }

    public Task<GateResult> ExitGate(PipelineContext context, StageResult result)
    {
        if (result.Outputs.TryGetValue("test_exit_code", out var code) && (int)code == 0)
            return Task.FromResult(GateResult.Ok());
        return Task.FromResult(GateResult.Fail("Tests did not pass."));
    }

    private static async Task<(int exitCode, string output)> RunDotnetTestAsync(
        string workDir, CancellationToken ct)
    {
        var dotnet = ImplementationStage.ResolveDotnet();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "test --no-build -v q",
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
}
