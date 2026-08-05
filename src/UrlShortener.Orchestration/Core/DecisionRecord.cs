namespace UrlShortener.Orchestration.Core;

public class DecisionRecord
{
    public string Stage { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;
}
