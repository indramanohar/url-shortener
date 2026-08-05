using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

[ApiController]
public class RedirectController(IUrlShortenerService svc) : ControllerBase
{
    // 302 (not 301): 301 is cached by browsers, which silently breaks click analytics on repeat visits.
    [HttpGet("{code}")]
    public async Task<IActionResult> RedirectToUrl(string code)
    {
        if (code.EndsWith("/analytics", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var link = await svc.ResolveAsync(code);
        if (link == null) return NotFound();

        _ = svc.RecordClickAsync(code);
        return new RedirectResult(link.OriginalUrl, permanent: false);
    }
}
