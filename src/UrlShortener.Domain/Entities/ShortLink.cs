namespace UrlShortener.Domain.Entities;

public class ShortLink
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ClickEvent> Clicks { get; set; } = new List<ClickEvent>();
}
