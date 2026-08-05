using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Services;
using UrlShortener.Orchestration.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// Trade-off: SQLite for 24-hr constraint. SQL Server swap = change provider + connection string.
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=urlshortener.db"));

builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();

// Rate limiting — limits are read lazily from IConfiguration inside the partition factory
// so test factories can override them via AddInMemoryCollection before first request.
// Trade-off: fixed window (simpler than sliding); sliding window avoids burst spikes at
// window boundaries and is the production preference.
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (ctx, ct) =>
    {
        var windowSec = ctx.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetValue("RateLimiting:WriteWindowSeconds", 60);
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers["Retry-After"] =
            ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? ((int)retryAfter.TotalSeconds).ToString()
                : windowSec.ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Rate limit exceeded.", retryAfterSeconds = windowSec }, ct);
    };

    // Per API key — write operations (shorten, delete)
    options.AddPolicy("write-per-key", httpContext =>
    {
        var cfg      = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        int limit    = cfg.GetValue("RateLimiting:WritePerKeyLimit",    60);
        int windowSec = cfg.GetValue("RateLimiting:WriteWindowSeconds", 60);
        var key      = httpContext.Request.Headers["X-Api-Key"].ToString();
        var partition = string.IsNullOrWhiteSpace(key) ? "anonymous" : key;
        return RateLimitPartition.GetFixedWindowLimiter(partition,
            _ => new FixedWindowRateLimiterOptions
            {
                Window               = TimeSpan.FromSeconds(windowSec),
                PermitLimit          = limit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });

    // Per client IP — redirect hot path
    options.AddPolicy("redirect-per-ip", httpContext =>
    {
        var cfg       = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        int limit     = cfg.GetValue("RateLimiting:RedirectPerIpLimit",    300);
        int windowSec = cfg.GetValue("RateLimiting:RedirectWindowSeconds", 60);
        var ip        = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip,
            _ => new FixedWindowRateLimiterOptions
            {
                Window               = TimeSpan.FromSeconds(windowSec),
                PermitLimit          = limit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });
});

// Orchestration engine — singletons so pipeline runs and approvals survive across requests
builder.Services.AddSingleton<PipelineApprovalService>();
builder.Services.AddSingleton<PipelineRunStore>();
builder.Services.AddSingleton<PipelineOrchestrator>(sp =>
{
    var approval = sp.GetRequiredService<PipelineApprovalService>();
    return new PipelineOrchestrator(approval);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.MapControllers();
app.Run();

// Expose for WebApplicationFactory in integration tests
public partial class Program { }
