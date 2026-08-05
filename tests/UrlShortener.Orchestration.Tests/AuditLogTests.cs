using UrlShortener.Orchestration.AuditLog;

namespace UrlShortener.Orchestration.Tests;

public class AuditLogTests
{
    [Fact]
    public async Task Append_CreatesChainedEntries()
    {
        var log = new HashChainedAuditLog();

        await log.AppendAsync(AuditEventType.PipelineStarted, "Pipeline", "started");
        await log.AppendAsync(AuditEventType.StageEntered, "Requirements", "entering");
        await log.AppendAsync(AuditEventType.StageCompleted, "Requirements", "done");

        Assert.Equal(3, log.Entries.Count);
        Assert.Equal(string.Empty, log.Entries[0].PreviousHash);
        Assert.Equal(log.Entries[0].Hash, log.Entries[1].PreviousHash);
        Assert.Equal(log.Entries[1].Hash, log.Entries[2].PreviousHash);
    }

    [Fact]
    public async Task VerifyChain_ReturnsTrue_OnUntamperedLog()
    {
        var log = new HashChainedAuditLog();
        await log.AppendAsync(AuditEventType.PipelineStarted, "Pipeline", "started");
        await log.AppendAsync(AuditEventType.StageEntered, "Design", "entering");

        Assert.True(log.VerifyChain());
    }

    [Fact]
    public async Task VerifyChain_ReturnsFalse_WhenEntryTampered()
    {
        var log = new HashChainedAuditLog();
        await log.AppendAsync(AuditEventType.PipelineStarted, "Pipeline", "started");
        await log.AppendAsync(AuditEventType.StageCompleted, "Requirements", "done");

        // Tamper with message on first entry — hash no longer matches
        var entry = log.Entries[0];
        var field = typeof(AuditEntry).GetProperty("Message")!;
        field.SetValue(entry, "TAMPERED");

        Assert.False(log.VerifyChain());
    }

    [Fact]
    public async Task Entries_AreSequential()
    {
        var log = new HashChainedAuditLog();
        for (int i = 0; i < 5; i++)
            await log.AppendAsync(AuditEventType.RetryAttempt, "Testing", $"attempt {i}");

        var seqs = log.Entries.Select(e => e.Sequence).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, seqs);
    }

    [Fact]
    public async Task EachEntry_HasNonEmptyHash()
    {
        var log = new HashChainedAuditLog();
        await log.AppendAsync(AuditEventType.PipelineStarted, "Pipeline", "started");

        Assert.NotEmpty(log.Entries[0].Hash);
        Assert.Equal(64, log.Entries[0].Hash.Length); // SHA-256 hex = 64 chars
    }
}
