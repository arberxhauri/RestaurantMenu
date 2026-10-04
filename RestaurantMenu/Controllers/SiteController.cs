using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// A restaurant's website: /site/{slug} on the app, or "/" on its own domain (SiteHostMiddleware
/// rewrites to here). Built from the dashboard: name, photos, colours, hours, featured dishes,
/// bookings; the Website page adds a tagline, an about text and social links. Unpublished sites
/// are visible only to the branch's team, as a preview.
/// </summary>
public class SiteController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly SeoService _seo;
    private readonly SiteHosts _hosts;
    private readonly IBranchAccess _access;

    public SiteController(ApplicationDbContext context, SeoService seo, SiteHosts hosts, IBranchAccess access)
    {
        _context = context;
        _seo = seo;
        _hosts = hosts;
        _access = access;
    }

    private Task<Branch?> BranchAsync(string slug)
    {
        var key = Uri.UnescapeDataString(slug).Trim().ToLower();
        return _context.Branches.AsNoTracking()
            .Include(b => b.OpeningHours)
            .Include(b => b.Categories!).ThenInclude(c => c.Products)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Name.Replace(" ", "").ToLower() == key);
    }

    /// <summary>The site's address for canonical links: its own domain, its subdomain, or /site/{slug}.</summary>
    private async Task<string> SiteUrlAsync(Branch b, string? lang)
    {
        var host = await _hosts.PublicHostAsync(b.Id, SeoService.Slug(b.Name));
        var query = lang == null || lang == SeoService.DefaultLanguage ? "" : $"?lang={lang}";
        return host != null ? $"https://{host}/{query}" : _seo.Url($"/site/{SeoService.Slug(b.Name)}") + query;
    }

    [HttpGet("site/{slug}")]
    public async Task<IActionResult> Index(string slug, string? lang)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var canonicalSlug = SeoService.Slug(branch.Name);
        if (!string.Equals(slug, canonicalSlug, StringComparison.Ordinal))
            return RedirectPermanent($"/site/{canonicalSlug}{Request.QueryString}");

        var site = await _context.BranchSites.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branch.Id) ?? new BranchSite { BranchId = branch.Id };
        var preview = !site.Enabled;
        if (preview && !(User.Identity?.IsAuthenticated == true && await _access.CanAsync(branch.Id, BranchPermission.EditBranch)))
            return NotFound();

        var languages = branch.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var language = lang != null && languages.Contains(lang) ? lang : languages.FirstOrDefault() ?? "en";

        // Dishes to show: featured ones guests can order now, else dishes with photos, in menu order.
        var zone = OpeningHours.Zone(branch.TimeZone);
        var now = DateTime.UtcNow;
        var dishes = (branch.Categories ?? new List<Category>()).OrderBy(c => c.Priority)
            .Where(c => ServingTimes.Status(c, zone, now) is not { IsOpen: false } || !c.HideWhenUnavailable)
            .SelectMany(c => (c.Products ?? new List<Product>()).OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id))
            .Where(p => p.IsAvailable).ToList();
        var highlights = dishes.Where(p => p.IsFeatured).Take(6).ToList();
        if (highlights.Count < 3) highlights = highlights.Concat(dishes.Where(p => !p.IsFeatured && !string.IsNullOrEmpty(p.Image))).Take(6).ToList();

        var bookingSettings = await _context.ReservationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branch.Id);
        var onOwnHost = HttpContext.Items.ContainsKey(SiteHostMiddleware.ItemKey);
        var langQuery = language == SeoService.DefaultLanguage ? "" : $"?lang={language}";
        var siteUrl = await SiteUrlAsync(branch, language);

        ViewData["Seo"] = new SeoMetadata
        {
            Title = string.IsNullOrWhiteSpace(site.Tagline) ? branch.Name : $"{branch.Name} · {TranslationHelper.GetTranslation(site.Tagline, site.TaglineTranslations, language)}",
            Description = Describe(branch, site, language),
            CanonicalUrl = siteUrl,
            ImageUrl = !string.IsNullOrWhiteSpace(branch.Banner) ? _seo.Absolute(branch.Banner) : !string.IsNullOrWhiteSpace(branch.Logo) ? _seo.Absolute(branch.Logo) : null,
            ImageAlt = branch.Name,
            OgType = "restaurant.restaurant",
            Locale = language,
            FaviconUrl = string.IsNullOrWhiteSpace(branch.Logo) ? "/logo.png" : branch.Logo,
            NoIndex = preview,
            Alternates = languages.Length > 1 ? await Task.WhenAll(languages.Select(async l => new SeoAlternate(l, await SiteUrlAsync(branch, l)))) : Array.Empty<SeoAlternate>(),
            JsonLd = StructuredData.ForBranchSite(branch, site, siteUrl, _seo.MenuUrl(branch.Name, language),
                bookingSettings != null && BookingService.IsBookable(branch, bookingSettings), _seo)
        };

        return View(new SitePage(branch, site, language, languages, preview, highlights,
            MenuUrl: (onOwnHost ? "/menu" : $"/menu/{canonicalSlug}") + langQuery,
            BookUrl: bookingSettings != null && BookingService.IsBookable(branch, bookingSettings) ? (onOwnHost ? "/book" : $"/book/{canonicalSlug}") + langQuery : null,
            Status: branch.HoursEnabled && branch.OpeningHours?.Any() == true ? OpeningHours.GetStatus(branch.OpeningHours, zone, now) : null,
            Currency: CurrencyHelper.GetCurrencySymbol(branch.Currency)));
    }

    private static string Describe(Branch b, BranchSite s, string lang)
    {
        var about = TranslationHelper.GetTranslation(s.About ?? "", s.AboutTranslations, lang);
        var tagline = TranslationHelper.GetTranslation(s.Tagline ?? "", s.TaglineTranslations, lang);
        var text = !string.IsNullOrWhiteSpace(about) ? about : !string.IsNullOrWhiteSpace(tagline) ? tagline : $"{b.Name}, {b.Address}. Menu, opening hours and bookings.";
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length > 160 ? text[..157].TrimEnd() + "…" : text;
    }

    /// <summary>robots.txt on a restaurant's own domain.</summary>
    [HttpGet("site/{slug}/robots.txt")]
    public async Task<IActionResult> Robots(string slug)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var url = await SiteUrlAsync(branch, null);
        return Content($"User-agent: *\nAllow: /\n\nSitemap: {url.TrimEnd('/')}/sitemap.xml\n", "text/plain", Encoding.UTF8);
    }

    /// <summary>sitemap.xml on a restaurant's own domain: the site in each language.</summary>
    [HttpGet("site/{slug}/sitemap.xml")]
    public async Task<IActionResult> Sitemap(string slug)
    {
        var branch = await BranchAsync(slug);
        if (branch == null || !await _context.BranchSites.AnyAsync(s => s.BranchId == branch.Id && s.Enabled)) return NotFound();
        var languages = branch.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var l in languages.DefaultIfEmpty("en"))
            sb.Append($"  <url><loc>{System.Security.SecurityElement.Escape(await SiteUrlAsync(branch, l))}</loc><changefreq>weekly</changefreq></url>\n");
        sb.Append("</urlset>\n");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}

public record SitePage(Branch Branch, BranchSite Site, string Language, string[] Languages, bool Preview,
    IReadOnlyList<Product> Highlights, string MenuUrl, string? BookUrl, OpeningHours.Status? Status, string Currency);
