using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using RestaurantMenu.Helpers;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// robots.txt and the sitemaps of the app's own host. Generated rather than static so the
/// sitemaps track restaurants as they change and every URL uses the host the site is really
/// served from. A restaurant's own domain gets its own (SiteController, via SiteHostMiddleware).
/// </summary>
public class SeoController : Controller
{
    private readonly SeoService _seo;
    private readonly SitemapService _sitemaps;

    public SeoController(SeoService seo, SitemapService sitemaps)
    {
        _seo = seo;
        _sitemaps = sitemaps;
    }

    [HttpGet("robots.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots() =>
        Content(RobotsTxt.Build(_seo.Url("/sitemap.xml"), RobotsTxt.AppOnlyPaths), "text/plain", Encoding.UTF8);

    [HttpGet("sitemap.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap() => Xml(await _sitemaps.IndexAsync());

    [HttpGet("sitemaps/{name:regex(^(pages|menus|sites)$)}.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> File(string name) =>
        await _sitemaps.FileAsync(name) is { } doc ? Xml(doc) : NotFound();

    [HttpGet("sitemaps/menus-{part:int}.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> MenuPart(int part) =>
        part >= 2 && await _sitemaps.FileAsync("menus", part) is { } doc ? Xml(doc) : NotFound();

    private ContentResult Xml(XDocument doc) =>
        Content(doc.Declaration + "\n" + doc.Root, "application/xml", Encoding.UTF8);
}
