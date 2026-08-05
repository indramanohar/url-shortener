namespace UrlShortener.Orchestration.Core;

public interface IPipelineApprovalService
{
    // Creates and registers a TCS for the given run+stage. Caller awaits the returned task.
    TaskCompletionSource<bool> Register(Guid runId, string stageName);

    // Called by the API endpoint when the human acts. Returns false if no pending TCS found.
    bool Resolve(Guid runId, bool approved);

    void Remove(Guid runId);

    bool HasPending(Guid runId);
}
