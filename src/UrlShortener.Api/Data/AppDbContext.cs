using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortLink>(e =>
        {
            e.HasIndex(s => s.Code).IsUnique();
            e.Property(s => s.Code).HasMaxLength(20);
            e.Property(s => s.OriginalUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<ClickEvent>(e =>
        {
            e.HasOne(c => c.ShortLink)
             .WithMany(s => s.Clicks)
             .HasForeignKey(c => c.ShortLinkId);
        });
    }
}
