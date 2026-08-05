using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UrlShortener.Api.Tests;

public class PipelineControllerTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string ApiKey = "pipeline-test-key";

    // ── Auth enforcement ──

    [Fact]
    public async Task Run_Returns401_WithoutApiKey()
    {
        var res = await _client.PostAsJsonAsync("/pipeline/run", new { scenario = "greenfield" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Approve_Returns401_WithoutApiKey()
    {
        var res = await _client.PostAsync($"/pipeline/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Reject_Returns401_WithoutApiKey()
    {
        var res = await _client.PostAsync($"/pipeline/{Guid.NewGuid()}/reject", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns401_WithoutApiKey()
    {
        var res = await _client.PostAsync($"/pipeline/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── Not found ──

    [Fact]
    public async Task GetStatus_Returns404_ForUnknownId()
    {
        var res = await _client.GetAsync($"/pipeline/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Approve_Returns404_ForUnknownId()
    {
        var res = await WithKey().PostAsync($"/pipeline/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns404_ForUnknownId()
    {
        var res = await WithKey().PostAsync($"/pipeline/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ── Start run ──

    [Fact]
    public async Task Run_Returns202_WithExpectedShape()
    {
        var res = await WithKey().PostAsJsonAsync("/pipeline/run",
            new { scenario = "greenfield", skipVulnScan = true });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.True(doc.TryGetProperty("runId", out _), "response must include runId");
        Assert.True(doc.TryGetProperty("approveUrl", out _), "response must include approveUrl");
        Assert.True(doc.TryGetProperty("cancelUrl", out _), "response must include cancelUrl");
    }

    // ── List runs ──

    [Fact]
    public async Task ListRuns_Returns200_WithArray()
    {
        var res = await _client.GetAsync("/pipeline");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Array, doc.ValueKind);
    }

    // ── Cancel ──

    [Fact]
    public async Task Cancel_StopsRunningPipeline_AndLogsToAudit()
    {
        // Start a run (pipeline will block at Design approval gate)
        var startRes = await WithKey().PostAsJsonAsync("/pipeline/run",
            new { scenario = "greenfield", skipVulnScan = true });
        Assert.Equal(HttpStatusCode.Accepted, startRes.StatusCode);

        var startDoc = JsonDocument.Parse(await startRes.Content.ReadAsStringAsync()).RootElement;
        var runId = startDoc.GetProperty("runId").GetString()!;

        // Give the background pipeline time to reach Design gate
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Cancel it
        var cancelRes = await WithKey().PostAsync($"/pipeline/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Poll until no longer Running (max ~5 seconds)
        string? finalStatus = null;
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var statusDoc = JsonDocument.Parse(
                await (await _client.GetAsync($"/pipeline/{runId}")).Content.ReadAsStringAsync()
            ).RootElement;
            finalStatus = statusDoc.GetProperty("status").GetString();
            if (finalStatus != "Running") break;
        }

        Assert.Equal("Cancelled", finalStatus);

        // Verify audit log contains the Cancelled event
        var auditRes = await _client.GetAsync($"/pipeline/{runId}/audit");
        var auditDoc = JsonDocument.Parse(await auditRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VALID", auditDoc.GetProperty("chainIntegrity").GetString());

        var hasCancelledEntry = auditDoc.GetProperty("entries").EnumerateArray()
            .Any(e => e.GetProperty("eventType").GetInt32() == (int)UrlShortener.Orchestration.AuditLog.AuditEventType.Cancelled);
        Assert.True(hasCancelledEntry, "Audit log must contain a Cancelled event");
    }

    // ── Approve after cancel ──

    [Fact]
    public async Task Approve_Returns409_WhenNoPendingApproval()
    {
        // Start and immediately cancel so no approval gate is pending
        var startRes = await WithKey().PostAsJsonAsync("/pipeline/run",
            new { scenario = "greenfield", skipVulnScan = true });
        var runId = JsonDocument.Parse(await startRes.Content.ReadAsStringAsync())
            .RootElement.GetProperty("runId").GetString()!;

        await Task.Delay(TimeSpan.FromSeconds(2));
        await WithKey().PostAsync($"/pipeline/{runId}/cancel", null);

        // Wait for Cancelled status
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var s = JsonDocument.Parse(
                await (await _client.GetAsync($"/pipeline/{runId}")).Content.ReadAsStringAsync()
            ).RootElement.GetProperty("status").GetString();
            if (s != "Running") break;
        }

        // Now approve should 409 — no approval is pending on a cancelled run
        var approveRes = await WithKey().PostAsync($"/pipeline/{runId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, approveRes.StatusCode);
    }

    // ── Cancel non-running run ──

    [Fact]
    public async Task Cancel_Returns409_WhenRunAlreadyCancelled()
    {
        var startRes = await WithKey().PostAsJsonAsync("/pipeline/run",
            new { scenario = "greenfield", skipVulnScan = true });
        var runId = JsonDocument.Parse(await startRes.Content.ReadAsStringAsync())
            .RootElement.GetProperty("runId").GetString()!;

        await Task.Delay(TimeSpan.FromSeconds(2));
        await WithKey().PostAsync($"/pipeline/{runId}/cancel", null);

        // Wait for Cancelled status
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var s = JsonDocument.Parse(
                await (await _client.GetAsync($"/pipeline/{runId}")).Content.ReadAsStringAsync()
            ).RootElement.GetProperty("status").GetString();
            if (s != "Running") break;
        }

        // Second cancel should 409 — already not running
        var res = await WithKey().PostAsync($"/pipeline/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    private HttpClient WithKey()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }
}
