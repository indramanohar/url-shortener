namespace UrlShortener.Orchestration.AuditLog;

public class AuditEntry
{
    public int Sequence { get; set; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public AuditEventType EventType { get; init; }
    public string StageName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();

    // Hash-chain fields — port of Python prototype pattern
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    // Canonical string used as the hash input (excludes Hash itself)
    public string ToHashInput() =>
        $"{Sequence}|{Timestamp:O}|{EventType}|{StageName}|{Message}|{PreviousHash}";
}
