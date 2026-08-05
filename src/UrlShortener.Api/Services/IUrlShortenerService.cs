using UrlShortener.Domain.DTOs;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Api.Services;

public interface IUrlShortenerService
{
    Task<ShortenResponse> ShortenAsync(ShortenRequest request, string apiKey, string baseUrl);
    Task<ShortLink?> ResolveAsync(string code);
    Task RecordClickAsync(string code);
    Task<bool> DeleteAsync(string code, string apiKey);
    Task<AnalyticsResponse?> GetAnalyticsAsync(string code);
}
