namespace UrlShortener.Orchestration.AuditLog;

public enum AuditEventType
{
    PipelineStarted,
    StageEntered,
    StageCompleted,
    StageFailed,
    RetryScheduled,
    RetryAttempt,
    RolledBack,
    ApprovalPending,
    ApprovalDecision,
    ApprovalTimeout,
    SafeStop,
    PipelineCompleted
}
