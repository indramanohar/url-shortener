namespace UrlShortener.Orchestration.Core;

public enum PipelineStatus { Running, Completed, SafeStopped, Cancelled }

public class PipelineRunResult
{
    public PipelineStatus Status { get; init; }
    public string? StoppedReason { get; init; }
    public int RollbackCount { get; init; }

    public static PipelineRunResult Completed() => new() { Status = PipelineStatus.Completed };
    public static PipelineRunResult SafeStopped(string reason) =>
        new() { Status = PipelineStatus.SafeStopped, StoppedReason = reason };
    public static PipelineRunResult Cancelled(string reason) =>
        new() { Status = PipelineStatus.Cancelled, StoppedReason = reason };
}
