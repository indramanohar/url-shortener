using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrlShortener.Api.Data;
using UrlShortener.Domain.DTOs;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Api.Services;

// Trade-off: IMemoryCache is node-local. Production swap = IDistributedCache + Redis.
public class UrlShortenerService(AppDbContext db, IMemoryCache cache) : IUrlShortenerService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<ShortenResponse> ShortenAsync(ShortenRequest request, string apiKey, string baseUrl)
    {
        var code = request.Alias ?? await GenerateUniqueCodeAsync();

        // Idempotent: same URL + same alias returns existing record
        var existing = await db.ShortLinks.FirstOrDefaultAsync(s => s.Code == code);
        if (existing != null)
        {
            if (existing.OriginalUrl != request.Url)
                throw new InvalidOperationException($"Alias '{code}' is already in use.");
            return ToResponse(existing, baseUrl);
        }

        var link = new ShortLink
        {
            Code = code,
            OriginalUrl = request.Url,
            ApiKey = apiKey,
            ExpiresAt = request.TtlDays.HasValue ? DateTime.UtcNow.AddDays(request.TtlDays.Value) : null
        };

        db.ShortLinks.Add(link);
        await db.SaveChangesAsync();
        cache.Set(code, link, CacheTtl);
        return ToResponse(link, baseUrl);
    }

    public async Task<ShortLink?> ResolveAsync(string code)
    {
        if (cache.TryGetValue(code, out ShortLink? cached))
            return IsValid(cached!) ? cached : null;

        // Fallback to DB if cache miss
        var link = await db.ShortLinks.FirstOrDefaultAsync(s => s.Code == code);
        if (link != null && IsValid(link))
        {
            cache.Set(code, link, CacheTtl);
            return link;
        }
        return null;
    }

    public async Task RecordClickAsync(string code)
    {
        var link = await db.ShortLinks.FirstOrDefaultAsync(s => s.Code == code);
        if (link == null) return;
        db.ClickEvents.Add(new ClickEvent { ShortLinkId = link.Id });
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string code, string apiKey)
    {
        var link = await db.ShortLinks.FirstOrDefaultAsync(s => s.Code == code && s.ApiKey == apiKey);
        if (link == null) return false;
        link.IsActive = false;
        await db.SaveChangesAsync();
        cache.Remove(code);
        return true;
    }

    public async Task<AnalyticsResponse?> GetAnalyticsAsync(string code)
    {
        var link = await db.ShortLinks
            .Include(s => s.Clicks)
            .FirstOrDefaultAsync(s => s.Code == code);
        if (link == null) return null;

        return new AnalyticsResponse
        {
            Code = code,
            OriginalUrl = link.OriginalUrl,
            TotalClicks = link.Clicks.Count,
            ClickTimestamps = link.Clicks.Select(c => c.ClickedAt).OrderByDescending(t => t)
        };
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        string code;
        int attempts = 0;
        do
        {
            code = ShortCodeGenerator.Generate();
            attempts++;
            if (attempts > 10)
                throw new InvalidOperationException("Failed to generate unique short code.");
        } while (await db.ShortLinks.AnyAsync(s => s.Code == code));
        return code;
    }

    private static bool IsValid(ShortLink link) =>
        link.IsActive && (link.ExpiresAt == null || link.ExpiresAt > DateTime.UtcNow);

    private static ShortenResponse ToResponse(ShortLink link, string baseUrl) => new()
    {
        Code = link.Code,
        ShortUrl = $"{baseUrl}/{link.Code}",
        OriginalUrl = link.OriginalUrl,
        CreatedAt = link.CreatedAt,
        ExpiresAt = link.ExpiresAt
    };
}
