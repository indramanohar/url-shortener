namespace UrlShortener.Domain.DTOs;

public class ShortenRequest
{
    public string Url { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public int? TtlDays { get; set; }
}
