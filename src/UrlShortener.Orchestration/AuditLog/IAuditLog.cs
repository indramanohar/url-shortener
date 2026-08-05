namespace UrlShortener.Orchestration.AuditLog;

public interface IAuditLog
{
    Task AppendAsync(AuditEventType eventType, string stageName, string message,
        Dictionary<string, string>? metadata = null);

    IReadOnlyList<AuditEntry> Entries { get; }

    bool VerifyChain();
}
