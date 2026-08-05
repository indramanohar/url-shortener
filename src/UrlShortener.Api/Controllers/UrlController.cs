using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Services;
using UrlShortener.Domain.DTOs;

namespace UrlShortener.Api.Controllers;

[ApiController]
public class UrlController(IUrlShortenerService svc) : ControllerBase
{
    [HttpPost("shorten")]
    public async Task<IActionResult> Shorten([FromBody] ShortenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest("A valid absolute URL is required.");

        var apiKey = GetApiKey();
        if (apiKey == null) return Unauthorized("X-Api-Key header is required.");

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await svc.ShortenAsync(request, apiKey, baseUrl);
            return CreatedAtAction(nameof(GetAnalytics), new { code = result.Code }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code)
    {
        var apiKey = GetApiKey();
        if (apiKey == null) return Unauthorized("X-Api-Key header is required.");

        var deleted = await svc.DeleteAsync(code, apiKey);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{code}/analytics")]
    public async Task<IActionResult> GetAnalytics(string code)
    {
        var result = await svc.GetAnalyticsAsync(code);
        return result == null ? NotFound() : Ok(result);
    }

    private string? GetApiKey() =>
        Request.Headers.TryGetValue("X-Api-Key", out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.ToString() : null;
}
