using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Tests;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove ALL existing AppDbContext / options registrations
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            // Capture the path once — re-evaluating inside the lambda would produce a new GUID
            // per options build, so migration and queries would target different files.
            var dbPath = $"Data Source=test-{Guid.NewGuid()}.db";
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(dbPath));
        });

        builder.UseEnvironment("Development");
    }
}
