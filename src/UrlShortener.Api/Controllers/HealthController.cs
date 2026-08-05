using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            await db.Database.CanConnectAsync();
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
        catch
        {
            return StatusCode(503, new { status = "unhealthy" });
        }
    }
}
