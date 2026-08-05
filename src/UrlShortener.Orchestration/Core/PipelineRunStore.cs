using System.Collections.Concurrent;

namespace UrlShortener.Orchestration.Core;

public class PipelineRunStore
{
    private readonly ConcurrentDictionary<Guid, PipelineRunState> _runs = new();

    public void Add(PipelineRunState run) => _runs[run.Id] = run;

    public PipelineRunState? Get(Guid id) => _runs.TryGetValue(id, out var r) ? r : null;

    public IEnumerable<PipelineRunState> All() => _runs.Values.OrderByDescending(r => r.StartedAt);
}
