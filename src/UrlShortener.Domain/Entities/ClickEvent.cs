namespace UrlShortener.Domain.Entities;

public class ClickEvent
{
    public int Id { get; set; }
    public int ShortLinkId { get; set; }
    public DateTime ClickedAt { get; set; } = DateTime.UtcNow;

    public ShortLink ShortLink { get; set; } = null!;
}
