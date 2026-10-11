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
    private readonly BranchSlugs _slugs;
    private readonly RestaurantSeo _restaurantSeo;

    public SiteController(ApplicationDbContext context, SeoService seo, SiteHosts hosts, IBranchAccess access, BranchSlugs slugs, RestaurantSeo restaurantSeo)
    {
        _restaurantSeo = restaurantSeo;
        _slugs = slugs;
        _context = context;
        _seo = seo;
        _hosts = hosts;
        _access = access;
    }

    /// <summary>The branch by its link or an old one; Index redirects old links to the current one.</summary>
    private async Task<Branch?> BranchAsync(string slug)
    {
        var match = await _slugs.ResolveAsync(slug);
        if (match == null) return null;
        return await _context.Branches.AsNoTracking()
            .Include(b => b.OpeningHours)
            .Include(b => b.Categories!).ThenInclude(c => c.Products)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == match.BranchId);
    }

    /// <summary>The site's address for canonical links: its own domain, its subdomain, or /site/{slug}.</summary>
    private async Task<string> SiteUrlAsync(Branch b, string? lang)
    {
        var host = await _hosts.PublicHostAsync(b.Id, b.Slug);
        var query = lang == null ? "" : SeoLanguages.Query(lang, SeoLanguages.Default(b));
        return host != null ? $"https://{host}/{query}" : _seo.Url($"/site/{b.Slug}") + query;
    }

    [HttpGet("site/{slug}")]
    public async Task<IActionResult> Index(string slug, string? lang)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var canonicalSlug = branch.Slug;
        if (!string.Equals(slug, canonicalSlug, StringComparison.Ordinal))
            return RedirectPermanent($"/site/{canonicalSlug}{Request.QueryString}");

        // One address per language: ?lang= the main language, or one the site doesn't have, goes to the bare URL.
        var languages = SeoLanguages.Of(branch);
        var mainLanguage = SeoLanguages.Default(branch);
        var (language, wrongLanguage) = SeoLanguages.Resolve(languages, mainLanguage, lang);
        var onOwnHost = HttpContext.Items.ContainsKey(SiteHostMiddleware.ItemKey);
        if (wrongLanguage)
            return RedirectPermanent((onOwnHost ? "/" : $"/site/{canonicalSlug}") + SeoLanguages.QueryWithout(Request.Query));

        // Not in the plan (or a paused branch, or a read-only account): the menu instead, for
        // now (302), so the address comes back as soon as the website does.
        if (!await _access.HasModuleAsync(branch.Id, BillingModule.Website))
            return Redirect($"/menu/{canonicalSlug}{Request.QueryString}");
        var bookingsOn = await _access.HasModuleAsync(branch.Id, BillingModule.Bookings);

        var site = await _context.BranchSites.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branch.Id) ?? new BranchSite { BranchId = branch.Id };
        var preview = !site.Enabled;
        if (preview && !(User.Identity?.IsAuthenticated == true && await _access.CanAsync(branch.Id, BranchPermission.EditBranch)))
            return NotFound();

        // Published, and the site has an address of its own (own domain or free subdomain): send
        // /site/{slug} there, so links and ranking build up on the one address guests know.
        if (!preview && !onOwnHost && await _hosts.PublicHostAsync(branch.Id, canonicalSlug) is { } ownHost)
            return RedirectPermanent($"https://{ownHost}/{SeoLanguages.Query(language, mainLanguage)}");

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
        var langQuery = SeoLanguages.Query(language, mainLanguage);
        var siteUrl = await SiteUrlAsync(branch, language);
        var bookable = bookingSettings != null && BookingService.IsBookable(branch, bookingSettings, bookingsOn);
        var about = TranslationHelper.GetTranslation(site.About ?? "", site.AboutTranslations, language);
        var tagline = TranslationHelper.GetTranslation(site.Tagline ?? "", site.TaglineTranslations, language);
        var image = !string.IsNullOrWhiteSpace(branch.ShareImage) ? branch.ShareImage
            : !string.IsNullOrWhiteSpace(branch.Banner) ? branch.Banner : branch.Logo;

        ViewData["Seo"] = new SeoMetadata
        {
            Title = SeoText.Own(branch.SeoTitle, branch.SeoTitleTranslations, language, SeoLanguages.BaseTextLanguage(branch))
                    ?? SeoText.SiteTitle(branch.Name, branch.BusinessType, branch.Cuisine, branch.City, language),
            Description = SeoText.Own(branch.SeoDescription, branch.SeoDescriptionTranslations, language, SeoLanguages.BaseTextLanguage(branch))
                          ?? SeoText.SiteDescription(branch.Name, about, tagline, branch.BusinessType, branch.Cuisine, branch.City, bookable, language),
            CanonicalUrl = siteUrl,
            ImageUrl = string.IsNullOrWhiteSpace(image) ? null : _seo.Absolute(ImageUrls.Share(image).Url),
            ImageWidth = string.IsNullOrWhiteSpace(image) ? null : ImageUrls.Share(image).Width,
            ImageHeight = string.IsNullOrWhiteSpace(image) ? null : ImageUrls.Share(image).Height,
            ImageAlt = branch.Name,
            SiteName = branch.Name,
            OgType = "restaurant.restaurant",
            Locale = language,
            FaviconUrl = string.IsNullOrWhiteSpace(branch.Logo) ? SeoMetadata.DefaultFavicon : branch.Logo,
            NoIndex = !SeoIndexing.Site(branch, !preview),
            Alternates = await Task.WhenAll(languages.Select(async l => new SeoAlternate(l, await SiteUrlAsync(branch, l)))),
            XDefaultUrl = await SiteUrlAsync(branch, null),
            LastModifiedUtc = new[] { branch.UpdatedUtc, site.UpdatedUtc }.Max(),
        };
        var seoMeta = (SeoMetadata)ViewData["Seo"]!;
        var facts = await _restaurantSeo.FactsAsync(branch, language, site,
            bookable ? _seo.BookUrl(branch, language) : null);
        seoMeta.JsonLd = StructuredData.ForBranchSite(facts, siteUrl, seoMeta.Title);

        return View(new SitePage(branch, site, language, languages, preview, highlights,
            MenuUrl: (onOwnHost ? "/menu" : $"/menu/{canonicalSlug}") + langQuery,
            BookUrl: bookingSettings != null && BookingService.IsBookable(branch, bookingSettings, bookingsOn) ? (onOwnHost ? "/book" : $"/book/{canonicalSlug}") + langQuery : null,
            Status: branch.HoursEnabled && branch.OpeningHours?.Any() == true ? OpeningHours.GetStatus(branch.OpeningHours, zone, now) : null,
            Currency: CurrencyHelper.GetCurrencySymbol(branch.Currency)));
    }

    /// <summary>robots.txt on a restaurant's own domain or subdomain: everything open, this host's sitemap.</summary>
    [HttpGet("site/{slug}/robots.txt")]
    public async Task<IActionResult> Robots(string slug)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        return Content(RobotsTxt.Build($"https://{Request.Host.Host}/sitemap.xml", Array.Empty<string>()), "text/plain", Encoding.UTF8);
    }

    /// <summary>
    /// sitemap.xml on a restaurant's own domain or subdomain: the pages whose main address is this
    /// host, in each language, with hreflang: the website (if published here) and, on its subdomain,
    /// the menu (SeoService.RestaurantHost). Empty when nothing here is indexable.
    /// </summary>
    [HttpGet("site/{slug}/sitemap.xml")]
    public async Task<IActionResult> Sitemap(string slug)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var host = Request.Host.Host;
        bool Here(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase);
        var languages = SeoLanguages.Of(branch);
        var main = SeoLanguages.Default(branch);
        var entries = new List<SitemapService.Entry>();

        var site = await _context.BranchSites.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branch.Id && s.Enabled);
        if (site != null && SeoIndexing.Site(branch, true) && await _access.HasModuleAsync(branch.Id, BillingModule.Website))
        {
            var alternates = new List<SeoAlternate>();
            foreach (var l in languages) alternates.Add(new SeoAlternate(l, await SiteUrlAsync(branch, l)));
            var images = string.IsNullOrWhiteSpace(branch.Banner) ? Array.Empty<string>() : new[] { _seo.Absolute(branch.Banner) };
            var lastModified = new[] { branch.UpdatedUtc, site.UpdatedUtc }.Max();
            foreach (var a in alternates.Where(a => Here(a.Url)))
                entries.Add(new SitemapService.Entry(a.Url, alternates, await SiteUrlAsync(branch, null), lastModified, a.Language == main ? images : Array.Empty<string>()));
        }

        var dishes = await _context.Products.CountAsync(p => p.BranchId == branch.Id && (p.IsAvailable || !branch.HideSoldOut));
        if (SeoIndexing.Menu(branch, dishes) && Here(_seo.MenuUrl(branch)))
        {
            var alternates = languages.Select(l => new SeoAlternate(l, _seo.MenuUrl(branch, l))).ToList();
            foreach (var a in alternates)
                entries.Add(new SitemapService.Entry(a.Url, alternates, _seo.MenuUrl(branch), branch.UpdatedUtc, Array.Empty<string>()));
        }

        var doc = SitemapService.UrlSet(entries, "weekly");
        return Content(doc.Declaration + "\n" + doc.Root, "application/xml", Encoding.UTF8);
    }
}

public record SitePage(Branch Branch, BranchSite Site, string Language, string[] Languages, bool Preview,
    IReadOnlyList<Product> Highlights, string MenuUrl, string? BookUrl, OpeningHours.Status? Status, string Currency);
