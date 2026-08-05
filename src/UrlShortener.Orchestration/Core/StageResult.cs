namespace UrlShortener.Orchestration.Core;

public record StageResult(bool Succeeded, Dictionary<string, object> Outputs, string? FailureReason = null)
{
    public static StageResult Ok(Dictionary<string, object>? outputs = null) =>
        new(true, outputs ?? new());
    public static StageResult Fail(string reason) =>
        new(false, new(), reason);
}
