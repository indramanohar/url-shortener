using System.Collections.Concurrent;

namespace UrlShortener.Orchestration.Core;

// Singleton. Single-instance, in-memory — does not survive restarts or scale horizontally.
// Documented trade-off: production would use a durable approval record in DB + SignalR/polling.
public class PipelineApprovalService : IPipelineApprovalService
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _pending = new();

    public TaskCompletionSource<bool> Register(Guid runId, string stageName)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[runId] = tcs;
        return tcs;
    }

    public bool Resolve(Guid runId, bool approved)
    {
        if (_pending.TryGetValue(runId, out var tcs))
        {
            tcs.TrySetResult(approved);
            return true;
        }
        return false;
    }

    public void Remove(Guid runId) => _pending.TryRemove(runId, out _);

    public bool HasPending(Guid runId) => _pending.ContainsKey(runId);
}
