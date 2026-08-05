using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Data;
using UrlShortener.Domain.DTOs;

namespace UrlShortener.Api.Tests;

// Separate factory: overrides rate limit to 2 per window so the third request reliably 429s.
public class RateLimitedFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:WritePerKeyLimit"]     = "2",
                ["RateLimiting:WriteWindowSeconds"]   = "60",
                ["RateLimiting:RedirectPerIpLimit"]   = "2",
                ["RateLimiting:RedirectWindowSeconds"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            var dbPath = $"Data Source=ratelimit-test-{Guid.NewGuid()}.db";
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(dbPath));
        });

        builder.UseEnvironment("Development");
    }
}

public class RateLimitTests(RateLimitedFactory factory) : IClassFixture<RateLimitedFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private const string ApiKey = "rl-test-key";

    // ── Write rate limit (POST /shorten, limit=2) ──

    [Fact]
    public async Task Shorten_Returns429_AfterLimitExceeded()
    {
        // Use a unique key per test so window state doesn't bleed between tests
        var key = $"key-{Guid.NewGuid()}";

        var r1 = await Shorten("https://example.com/a", key);
        var r2 = await Shorten("https://example.com/b", key);
        var r3 = await Shorten("https://example.com/c", key);

        Assert.Equal(HttpStatusCode.Created,              r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created,              r2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,      r3.StatusCode);
    }

    [Fact]
    public async Task Shorten_429_IncludesRetryAfterHeader()
    {
        var key = $"key-{Guid.NewGuid()}";
        await Shorten("https://example.com/x", key);
        await Shorten("https://example.com/y", key);
        var r3 = await Shorten("https://example.com/z", key);

        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        Assert.True(r3.Headers.Contains("Retry-After"), "Retry-After header must be present on 429");
    }

    [Fact]
    public async Task Shorten_DifferentKeys_HaveIndependentLimits()
    {
        // Each key gets its own fixed-window bucket — key-A exhausted should not affect key-B
        var keyA = $"key-A-{Guid.NewGuid()}";
        var keyB = $"key-B-{Guid.NewGuid()}";

        await Shorten("https://example.com/a1", keyA);
        await Shorten("https://example.com/a2", keyA);
        var aRejected = await Shorten("https://example.com/a3", keyA);

        var bOk = await Shorten("https://example.com/b1", keyB);

        Assert.Equal(HttpStatusCode.TooManyRequests, aRejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created,         bOk.StatusCode);
    }

    // ── Redirect rate limit (GET /{code}, limit=2) ──

    [Fact]
    public async Task Redirect_Returns429_AfterLimitExceeded()
    {
        // Create a link first (use a fresh key so the write limit is not a concern)
        var key = $"key-{Guid.NewGuid()}";
        var created = await (await Shorten("https://example.com/redirect-rl", key))
            .Content.ReadFromJsonAsync<ShortenResponse>();

        var r1 = await _client.GetAsync($"/{created!.Code}");
        var r2 = await _client.GetAsync($"/{created.Code}");
        var r3 = await _client.GetAsync($"/{created.Code}");

        Assert.Equal(HttpStatusCode.Redirect,        r1.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,        r2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
    }

    // ── Unrated endpoints are not affected ──

    [Fact]
    public async Task Health_IsNotRateLimited()
    {
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
    }

    private Task<HttpResponseMessage> Shorten(string url, string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/shorten")
        {
            Content = JsonContent.Create(new ShortenRequest { Url = url })
        };
        req.Headers.Add("X-Api-Key", apiKey);
        return _client.SendAsync(req);
    }
}
