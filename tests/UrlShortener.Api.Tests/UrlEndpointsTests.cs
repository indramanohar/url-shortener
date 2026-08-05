using System.Net;
using System.Net.Http.Json;
using UrlShortener.Domain.DTOs;

namespace UrlShortener.Api.Tests;

public class UrlEndpointsTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private const string ApiKey = "test-key";

    // ── POST /shorten ──

    [Fact]
    public async Task Shorten_Returns201_WithValidUrl()
    {
        var resp = await _client.SendAsync(ShortRequest("https://example.com/long/path"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ShortenResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Code);
        Assert.Contains(body.Code, body.ShortUrl);
    }

    [Fact]
    public async Task Shorten_Returns400_WithInvalidUrl()
    {
        var resp = await _client.SendAsync(ShortRequest("not-a-url"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Shorten_Returns401_WithoutApiKey()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/shorten")
        {
            Content = JsonContent.Create(new ShortenRequest { Url = "https://example.com" })
        };
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Shorten_IsIdempotent_SameUrlAndAlias()
    {
        var req1 = ShortRequest("https://example.com/idempotent", alias: "idem-alias");
        var req2 = ShortRequest("https://example.com/idempotent", alias: "idem-alias");

        var r1 = await (await _client.SendAsync(req1)).Content.ReadFromJsonAsync<ShortenResponse>();
        var r2 = await (await _client.SendAsync(req2)).Content.ReadFromJsonAsync<ShortenResponse>();

        Assert.Equal(r1!.Code, r2!.Code);
    }

    [Fact]
    public async Task Shorten_Returns409_WhenAliasConflicts()
    {
        await _client.SendAsync(ShortRequest("https://example.com/original", alias: "conflict-alias"));
        var resp = await _client.SendAsync(ShortRequest("https://different.com", alias: "conflict-alias"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // ── GET /{code} redirect ──

    [Fact]
    public async Task Redirect_Returns302_ForKnownCode()
    {
        var created = await (await _client.SendAsync(ShortRequest("https://example.com/redirect-target")))
            .Content.ReadFromJsonAsync<ShortenResponse>();

        var resp = await _client.GetAsync($"/{created!.Code}");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("https://example.com/redirect-target", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_Returns404_ForUnknownCode()
    {
        var resp = await _client.GetAsync("/doesnotexist");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── DELETE /{code} ──

    [Fact]
    public async Task Delete_Returns204_ForOwnedCode()
    {
        var created = await (await _client.SendAsync(ShortRequest("https://example.com/delete-me")))
            .Content.ReadFromJsonAsync<ShortenResponse>();

        var del = new HttpRequestMessage(HttpMethod.Delete, $"/{created!.Code}");
        del.Headers.Add("X-Api-Key", ApiKey);
        var resp = await _client.SendAsync(del);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns404_ForWrongApiKey()
    {
        var created = await (await _client.SendAsync(ShortRequest("https://example.com/wrong-key")))
            .Content.ReadFromJsonAsync<ShortenResponse>();

        var del = new HttpRequestMessage(HttpMethod.Delete, $"/{created!.Code}");
        del.Headers.Add("X-Api-Key", "wrong-key");
        var resp = await _client.SendAsync(del);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AfterDelete_Redirect_Returns404()
    {
        var created = await (await _client.SendAsync(ShortRequest("https://example.com/deactivated")))
            .Content.ReadFromJsonAsync<ShortenResponse>();

        var del = new HttpRequestMessage(HttpMethod.Delete, $"/{created!.Code}");
        del.Headers.Add("X-Api-Key", ApiKey);
        await _client.SendAsync(del);

        var resp = await _client.GetAsync($"/{created.Code}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── GET /{code}/analytics ──

    [Fact]
    public async Task Analytics_Returns200_WithClickData()
    {
        var created = await (await _client.SendAsync(ShortRequest("https://example.com/analytics-test")))
            .Content.ReadFromJsonAsync<ShortenResponse>();

        // Generate a click
        await _client.GetAsync($"/{created!.Code}");

        var resp = await _client.GetAsync($"/{created.Code}/analytics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AnalyticsResponse>();
        Assert.NotNull(body);
        Assert.Equal(created.Code, body.Code);
    }

    [Fact]
    public async Task Analytics_Returns404_ForUnknownCode()
    {
        var resp = await _client.GetAsync("/no-such-code/analytics");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── GET /health ──

    [Fact]
    public async Task Health_Returns200()
    {
        var resp = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Helper ──

    private static HttpRequestMessage ShortRequest(string url, string? alias = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/shorten")
        {
            Content = JsonContent.Create(new ShortenRequest { Url = url, Alias = alias })
        };
        req.Headers.Add("X-Api-Key", ApiKey);
        return req;
    }
}
