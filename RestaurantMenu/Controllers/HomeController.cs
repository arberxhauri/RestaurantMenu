using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HomeController> _logger;
    private readonly SeoService _seo;
    private readonly MarketingService _marketing;

    public HomeController(ApplicationDbContext context, ILogger<HomeController> logger, SeoService seo, MarketingService marketing)
    {
        _context = context;
        _logger = logger;
        _seo = seo;
        _marketing = marketing;
    }

    public async Task<IActionResult> Index(string? lang)
    {
        // "/" is the home page's one address: /home and /home/index are the same page.
        var (language, wrongLanguage) = MarketingPages.Resolve(MarketingPages.Get("home"), lang);
        if (Request.Path != "/" || wrongLanguage)
        {
            return RedirectPermanent("/" + SeoLanguages.Query(language, "en"));
        }

        // Partners strip: restaurants with a logo that are happy to be found (not demos).
        ViewBag.Branches = await _context.Branches
            .Where(b => !string.IsNullOrEmpty(b.Logo) && b.ShowInSearch && !b.IsDemo)
            .OrderBy(b => b.Name)
            .ToListAsync();

        var t = MarketingText.For(language);
        var chrome = await _marketing.ChromeAsync(language, "/");
        var prices = await _marketing.PricesAsync();
        ViewBag.Prices = prices;
        ViewData["Marketing"] = chrome;
        ViewData["Seo"] = MarketingSeo(t.HomeTitle, string.Format(t.HomeDescription, chrome.TrialDays), language, MarketingPages.Get("home"),
            StructuredData.ForLandingPage(_seo, await _marketing.DefinitionAsync(language), language));

        return View();
    }

    /// <summary>Head tags for a marketing page in English (bare URL) or Albanian (?lang=sq).</summary>
    private SeoMetadata MarketingSeo(string title, string description, string language, MarketingPages.MarketingPage page, string? jsonLd) => new()
    {
        Title = title,
        Description = SeoText.Trim(description, SeoText.DescriptionMax),
        CanonicalUrl = _seo.Url(MarketingPages.PathFor(page, language)),
        Alternates = page.Languages.Select(l => new SeoAlternate(l, _seo.Url(MarketingPages.PathFor(page, l)))).ToList(),
        XDefaultUrl = _seo.Url(page.Path),
        Locale = language,
        ImageUrl = _seo.Absolute($"/img/og/{page.Key}-{language}.png"),
        ImageWidth = 1200,
        ImageHeight = 630,
        ImageAlt = title,
        OgType = "website",
        ThemeColor = "#D55C13",
        JsonLd = jsonLd
    };

    public async Task<IActionResult> Privacy()
    {
        ViewData["Marketing"] = await _marketing.ChromeAsync("en", "/home/privacy");
        ViewData["Title"] = "Privacy Policy";
        ViewData["Seo"] = new SeoMetadata
        {
            Title = "Privacy Policy — My Quick Menu",
            Description = "How My Quick Menu collects, uses and protects the data of restaurants and their guests.",
            CanonicalUrl = _seo.Url("/home/privacy"),
            OgType = "article",
            ThemeColor = "#D55C13"
        };

        return View();
    }

    public async Task<IActionResult> Terms()
    {
        ViewData["Marketing"] = await _marketing.ChromeAsync("en", "/home/terms");
        ViewData["Title"] = "Terms of use";
        ViewData["Seo"] = new SeoMetadata
        {
            Title = "Terms of use — My Quick Menu",
            Description = "The terms for restaurants using My Quick Menu and for guests using their menus, bookings and ordering.",
            CanonicalUrl = _seo.Url("/home/terms"),
            OgType = "article",
            ThemeColor = "#D55C13"
        };

        return View();
    }

    // Re-executed by UseStatusCodePagesWithReExecute for 404s and other error statuses.
    [NoIndex]
    [Route("home/status/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        var feature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>();
        var originalPath = feature?.OriginalPath ?? "";

        ViewData["Title"] = code == 404 ? "Not found" : "Something went wrong";
        // Out of the index, but its links (home, pricing) are followed.
        ViewData["Seo"] = new SeoMetadata
        {
            Title = (code == 404 ? "Page not found" : "Something went wrong") + " · My Quick Menu",
            Description = "This page isn't available. Find restaurant menus, websites and bookings on My Quick Menu.",
            NoIndex = true
        };
        ViewBag.Code = code;
        ViewBag.IsMenu = originalPath.StartsWith("/menu/", StringComparison.OrdinalIgnoreCase);
        Response.StatusCode = code;
        return View();
    }

    [NoIndex]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
