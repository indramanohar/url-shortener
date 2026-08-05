using UrlShortener.Orchestration.AuditLog;

namespace UrlShortener.Orchestration.Core;

// Contract from ARCHITECTURE.md — do not change the method signatures
public interface IPipelineStage
{
    string Name { get; }
    bool RequiresHumanApproval { get; }
    Task<GateResult> EntryGate(PipelineContext context);
    Task<StageResult> Execute(PipelineContext context);
    Task<GateResult> ExitGate(PipelineContext context, StageResult result);
}
