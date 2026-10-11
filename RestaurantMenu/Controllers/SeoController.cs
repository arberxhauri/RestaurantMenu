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
    private readonly MarketingService _marketing;

    public SeoController(SeoService seo, SitemapService sitemaps, MarketingService marketing)
    {
        _seo = seo;
        _sitemaps = sitemaps;
        _marketing = marketing;
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

    /// <summary>A short guide for AI assistants (llmstxt.org): what My Quick Menu is, its pages and prices.</summary>
    [HttpGet("llms.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Llms() =>
        Content(LlmsText.Summary(await LlmsInputAsync()), "text/plain", Encoding.UTF8);

    /// <summary>The full text of the content pages in English and Albanian, for AI assistants.</summary>
    [HttpGet("llms-full.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> LlmsFull() =>
        Content(LlmsText.Full(await LlmsInputAsync()), "text/plain", Encoding.UTF8);

    private async Task<LlmsText.Input> LlmsInputAsync()
    {
        var restaurants = (await _sitemaps.MenuEntriesAsync()).Select(e => e.XDefault).Distinct().Count();
        return new LlmsText.Input(_seo.BaseUrl, _marketing.Brand, await _marketing.TokensAsync("en"), await _marketing.TokensAsync("sq"), restaurants);
    }

    private ContentResult Xml(XDocument doc) =>
        Content(doc.Declaration + "\n" + doc.Root, "application/xml", Encoding.UTF8);
}
