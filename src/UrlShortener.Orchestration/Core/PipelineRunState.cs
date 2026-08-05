using UrlShortener.Orchestration.AuditLog;

namespace UrlShortener.Orchestration.Core;

public class PipelineRunState
{
    public Guid Id { get; init; }
    public string Scenario { get; init; } = string.Empty;
    public PipelineStatus Status { get; set; } = PipelineStatus.Running;
    public string? StoppedReason { get; set; }
    public HashChainedAuditLog AuditLog { get; init; } = null!;
    public string? CurrentStage { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
