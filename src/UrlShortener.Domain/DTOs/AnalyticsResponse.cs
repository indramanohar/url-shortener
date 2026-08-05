namespace UrlShortener.Domain.DTOs;

public class AnalyticsResponse
{
    public string Code { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public int TotalClicks { get; set; }
    public IEnumerable<DateTime> ClickTimestamps { get; set; } = Enumerable.Empty<DateTime>();
}
