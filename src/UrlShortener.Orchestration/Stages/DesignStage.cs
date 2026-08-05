using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Stages;

// RequiresHumanApproval = true: human reviews the design before Implementation is allowed to start.
// Design locks in structure that everything downstream depends on — this is the expensive-to-undo gate.
public class DesignStage : IPipelineStage
{
    public string Name => "Design";
    public bool RequiresHumanApproval => true;

    public Task<GateResult> EntryGate(PipelineContext context)
    {
        if (!context.HasArtifact("normalized_spec"))
            return Task.FromResult(GateResult.Fail("Requirements stage must complete before Design."));
        return Task.FromResult(GateResult.Ok());
    }

    public Task<StageResult> Execute(PipelineContext context)
    {
        var scenario = context.Scenario;

        var apiContracts = scenario switch
        {
            "greenfield" => """
                API Contracts:
                  POST /shorten        → 201 ShortenResponse | 400 | 401 | 409
                  GET  /{code}         → 302 Location header  | 404
                  DELETE /{code}       → 204                  | 401 | 404
                  GET  /health         → 200
                """,
            "brownfield" => """
                API Contracts (addendum to Greenfield):
                  GET /{code}/analytics → 200 AnalyticsResponse | 404
                  (GET /{code} now also fires async click record)
                """,
            "ambiguous" => """
                API Contracts (reliability additions):
                  GET /health → 200 { status, timestamp, db_reachable }
                  Rate limiting: 429 TooManyRequests on burst above threshold
                  Cache layer: transparent to callers, fallback to DB on miss
                """,
            _ => $"Custom design for: {scenario}"
        };

        var dataModel = """
            Entities:
              ShortLink  { Id, Code(unique), OriginalUrl, ApiKey, CreatedAt, ExpiresAt, IsActive }
              ClickEvent { Id, ShortLinkId(FK), ClickedAt }
            Index: ShortLink.Code (unique)
            """;

        var stackDecisions = """
            Stack decisions:
              Runtime:   .NET 8 / ASP.NET Core Web API
              ORM:       EF Core 8 + SQLite (trade-off: SQL Server in production)
              Cache:     IMemoryCache (trade-off: Redis in production)
              Tests:     xUnit + WebApplicationFactory
            """;

        return Task.FromResult(StageResult.Ok(new Dictionary<string, object>
        {
            ["api_contracts"] = apiContracts,
            ["data_model"] = dataModel,
            ["stack_decisions"] = stackDecisions,
            ["design_complete"] = true
        }));
    }

    public Task<GateResult> ExitGate(PipelineContext context, StageResult result)
    {
        if (!result.Outputs.ContainsKey("api_contracts"))
            return Task.FromResult(GateResult.Fail("api_contracts artifact missing."));
        if (!result.Outputs.ContainsKey("data_model"))
            return Task.FromResult(GateResult.Fail("data_model artifact missing."));
        if (!result.Outputs.ContainsKey("stack_decisions"))
            return Task.FromResult(GateResult.Fail("stack_decisions artifact missing."));
        return Task.FromResult(GateResult.Ok());
    }
}
