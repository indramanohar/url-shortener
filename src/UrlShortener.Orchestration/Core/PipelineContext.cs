using UrlShortener.Orchestration.AuditLog;

namespace UrlShortener.Orchestration.Core;

// Contract from ARCHITECTURE.md — extended with RunId, Scenario, CancellationToken
public class PipelineContext
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public string Scenario { get; init; } = string.Empty;
    public Dictionary<string, object> Artifacts { get; } = new();
    public List<DecisionRecord> Lineage { get; } = new();
    public IAuditLog AuditLog { get; init; } = null!;
    public CancellationToken CancellationToken { get; init; }

    public T? GetArtifact<T>(string key) =>
        Artifacts.TryGetValue(key, out var v) && v is T typed ? typed : default;

    public bool HasArtifact(string key) => Artifacts.ContainsKey(key);
}
