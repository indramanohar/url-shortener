using UrlShortener.Orchestration.AuditLog;
using UrlShortener.Orchestration.Core;
using UrlShortener.Orchestration.Stages;

namespace UrlShortener.Orchestration.Tests;

public class StageGateTests
{
    private static PipelineContext MakeContext(string scenario = "greenfield",
        Dictionary<string, object>? artifacts = null)
    {
        var ctx = new PipelineContext
        {
            Scenario = scenario,
            AuditLog = new HashChainedAuditLog()
        };
        if (artifacts != null)
            foreach (var (k, v) in artifacts)
                ctx.Artifacts[k] = v;
        return ctx;
    }

    // ── RequirementsStage ──

    [Fact]
    public async Task Requirements_EntryGate_Fails_WhenNoScenario()
    {
        var stage = new RequirementsStage();
        var ctx = MakeContext(scenario: "");
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Requirements_Execute_ProducesNormalizedSpec()
    {
        var stage = new RequirementsStage();
        var ctx = MakeContext("greenfield");
        var result = await stage.Execute(ctx);
        Assert.True(result.Succeeded);
        Assert.True(result.Outputs.ContainsKey("normalized_spec"));
        Assert.True(result.Outputs.ContainsKey("ambiguities_identified"));
    }

    [Fact]
    public async Task Requirements_ExitGate_Fails_WhenSpecMissing()
    {
        var stage = new RequirementsStage();
        var ctx = MakeContext();
        var result = await stage.ExitGate(ctx, StageResult.Ok(new Dictionary<string, object>()));
        Assert.False(result.Passed);
    }

    // ── DesignStage ──

    [Fact]
    public async Task Design_EntryGate_Fails_WhenNormalizedSpecMissing()
    {
        var stage = new DesignStage();
        var ctx = MakeContext();
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
        Assert.Contains("Requirements", result.Reason);
    }

    [Fact]
    public async Task Design_EntryGate_Passes_WhenSpecPresent()
    {
        var stage = new DesignStage();
        var ctx = MakeContext(artifacts: new() { ["normalized_spec"] = "spec" });
        var result = await stage.EntryGate(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Design_Execute_ProducesAllArtifacts()
    {
        var stage = new DesignStage();
        var ctx = MakeContext(artifacts: new() { ["normalized_spec"] = "spec" });
        var result = await stage.Execute(ctx);
        Assert.True(result.Succeeded);
        Assert.True(result.Outputs.ContainsKey("api_contracts"));
        Assert.True(result.Outputs.ContainsKey("data_model"));
        Assert.True(result.Outputs.ContainsKey("stack_decisions"));
    }

    [Fact]
    public void Design_RequiresHumanApproval()
    {
        Assert.True(new DesignStage().RequiresHumanApproval);
    }

    // ── ImplementationStage ──

    [Fact]
    public async Task Implementation_EntryGate_Fails_WhenDesignNotComplete()
    {
        var stage = new ImplementationStage();
        var ctx = MakeContext();
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Implementation_EntryGate_Passes_WhenDesignComplete()
    {
        var stage = new ImplementationStage();
        var ctx = MakeContext(artifacts: new() { ["design_complete"] = true });
        var result = await stage.EntryGate(ctx);
        Assert.True(result.Passed);
    }

    // ── TestingStage ──

    [Fact]
    public async Task Testing_EntryGate_Fails_WhenImplementationNotComplete()
    {
        var stage = new TestingStage();
        var ctx = MakeContext();
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Testing_Execute_ReturnsFailure_WhenInjectionFlagSet()
    {
        var stage = new TestingStage();
        var ctx = MakeContext(artifacts: new()
        {
            ["implementation_complete"] = true,
            ["inject_test_failure"] = true,
            ["solution_root"] = Directory.GetCurrentDirectory()
        });
        var result = await stage.Execute(ctx);
        Assert.False(result.Succeeded);
        Assert.Contains("INJECTED FAILURE", result.FailureReason);
    }

    // ── ReleaseStage ──

    [Fact]
    public void Release_RequiresHumanApproval()
    {
        Assert.True(new ReleaseStage().RequiresHumanApproval);
    }

    [Fact]
    public async Task Release_EntryGate_Fails_WhenTestingNotComplete()
    {
        var stage = new ReleaseStage();
        var ctx = MakeContext(artifacts: new() { ["documentation_complete"] = true });
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
        Assert.Contains("Testing", result.Reason);
    }

    [Fact]
    public async Task Release_EntryGate_Fails_WhenDocsNotComplete()
    {
        var stage = new ReleaseStage();
        var ctx = MakeContext(artifacts: new() { ["testing_complete"] = true });
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
        Assert.Contains("Documentation", result.Reason);
    }

    [Fact]
    public async Task Release_EntryGate_Passes_WhenBothComplete()
    {
        var stage = new ReleaseStage();
        var ctx = MakeContext(artifacts: new()
        {
            ["testing_complete"] = true,
            ["documentation_complete"] = true
        });
        var result = await stage.EntryGate(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Release_EntryGate_Fails_WhenSecretDetected()
    {
        var stage = new ReleaseStage();
        var ctx = MakeContext(artifacts: new()
        {
            ["testing_complete"] = true,
            ["documentation_complete"] = true,
            ["secret_detected"] = true
        });
        var result = await stage.EntryGate(ctx);
        Assert.False(result.Passed);
        Assert.Contains("secret", result.Reason!.ToLower());
    }
}
