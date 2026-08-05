namespace UrlShortener.Orchestration.Core;

public record GateResult(bool Passed, string? Reason = null)
{
    public static GateResult Ok() => new(true);
    public static GateResult Fail(string reason) => new(false, reason);
}
