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
    private const string LandingDescription =
        "Digital menus, your restaurant website, table bookings and day-to-day management " +
        "in one platform. Change a price once and it lands everywhere in seconds.";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<HomeController> _logger;
    private readonly SeoService _seo;

    public HomeController(ApplicationDbContext context, ILogger<HomeController> logger, SeoService seo)
    {
        _context = context;
        _logger = logger;
        _seo = seo;
    }

    public async Task<IActionResult> Index()
    {
        // Get all branches with logos for partners carousel
        var branches = await _context.Branches
            .Where(b => !b.IsDeleted && !string.IsNullOrEmpty(b.Logo))
            .OrderBy(b => b.Name)
            .ToListAsync();

        ViewBag.Branches = branches;

        ViewData["Seo"] = new SeoMetadata
        {
            Title = "My Quick Menu — digital menus, websites & bookings for restaurants",
            Description = LandingDescription,
            CanonicalUrl = _seo.Url("/"),
            ImageUrl = _seo.Absolute("/logo.png"),
            ImageAlt = "My Quick Menu",
            OgType = "website",
            ThemeColor = "#D55C13",
            JsonLd = StructuredData.ForLandingPage(_seo, LandingDescription)
        };

        return View();
    }

    public IActionResult Privacy()
    {
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

    public IActionResult Terms()
    {
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
