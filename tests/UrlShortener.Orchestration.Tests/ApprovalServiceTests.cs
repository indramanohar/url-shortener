using UrlShortener.Orchestration.Core;

namespace UrlShortener.Orchestration.Tests;

public class ApprovalServiceTests
{
    [Fact]
    public async Task Register_Then_Approve_ResolvesTrue()
    {
        var svc = new PipelineApprovalService();
        var id = Guid.NewGuid();
        var tcs = svc.Register(id, "Design");

        svc.Resolve(id, approved: true);
        var result = await tcs.Task;

        Assert.True(result);
    }

    [Fact]
    public async Task Register_Then_Reject_ResolvesFalse()
    {
        var svc = new PipelineApprovalService();
        var id = Guid.NewGuid();
        var tcs = svc.Register(id, "Release");

        svc.Resolve(id, approved: false);
        var result = await tcs.Task;

        Assert.False(result);
    }

    [Fact]
    public void Resolve_ReturnsFalse_WhenNoPendingApproval()
    {
        var svc = new PipelineApprovalService();
        var result = svc.Resolve(Guid.NewGuid(), approved: true);
        Assert.False(result);
    }

    [Fact]
    public void HasPending_ReturnsFalse_AfterRemove()
    {
        var svc = new PipelineApprovalService();
        var id = Guid.NewGuid();
        svc.Register(id, "Design");
        Assert.True(svc.HasPending(id));

        svc.Remove(id);
        Assert.False(svc.HasPending(id));
    }

    [Fact]
    public async Task WhenAny_Timeout_WinsOverPendingApproval()
    {
        var svc = new PipelineApprovalService();
        var id = Guid.NewGuid();
        var tcs = svc.Register(id, "Design");

        // Short timeout — no one calls Resolve
        var timeout = Task.Delay(TimeSpan.FromMilliseconds(50));
        var winner = await Task.WhenAny(tcs.Task, timeout);

        Assert.Equal(timeout, winner);
    }
}
