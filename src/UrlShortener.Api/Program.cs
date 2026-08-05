using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// Trade-off: SQLite for 24-hr constraint. SQL Server swap = change provider + connection string.
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=urlshortener.db"));

builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();

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

app.MapControllers();
app.Run();

// Expose for WebApplicationFactory in integration tests
public partial class Program { }
