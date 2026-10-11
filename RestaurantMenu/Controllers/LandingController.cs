using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Helpers.JsonLd;
using RestaurantMenu.Helpers.Landing;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// The marketing site's content pages (LandingContent: product pages, FAQ, about, contact) and
/// the restaurant directory (/restaurants), in English and Albanian (?lang=sq). Server-rendered,
/// no JavaScript needed for any content. docs/SEO-GEO.md, "Content pages".
/// </summary>
public class LandingController : Controller
{
    /// <summary>Restaurants per directory page.</summary>
    public const int PageSize = 60;

    private readonly ApplicationDbContext _db;
    private readonly SeoService _seo;
    private readonly MarketingService _marketing;
    private readonly IEntitlementService _plans;
    private readonly SiteHosts _hosts;

    public LandingController(ApplicationDbContext db, SeoService seo, MarketingService marketing, IEntitlementService plans, SiteHosts hosts)
    {
        _db = db;
        _seo = seo;
        _marketing = marketing;
        _plans = plans;
        _hosts = hosts;
    }

    [HttpGet("{key:regex(^(digital-menu|restaurant-website|table-bookings|restaurant-management|faq|about|contact)$)}")]
    public async Task<IActionResult> Page(string key, string? lang)
    {
        var registry = MarketingPages.Get(key);
        var (language, wrongLanguage) = MarketingPages.Resolve(registry, lang);
        if (wrongLanguage) return RedirectPermanent(registry.Path + SeoLanguages.QueryWithout(Request.Query));

        var page = LandingContent.Get(key, language);
        var tokens = await _marketing.TokensAsync(language);
        var chrome = await _marketing.ChromeAsync(language, registry.Path);
        var t = chrome.T;
        var canonical = _seo.Url(MarketingPages.PathFor(registry, language));
        var title = LandingContent.Fill(page.MetaTitle, tokens);
        var faqs = key == "faq"
            ? LandingContent.FaqGroups(language).SelectMany(g => g.Faqs).ToList()
            : (page.Faqs ?? Array.Empty<LandingContent.Faq>()).ToList();
        var isProduct = key is "digital-menu" or "restaurant-website" or "table-bookings" or "restaurant-management";

        var seo = Seo(registry, language, title, LandingContent.Fill(page.MetaDescription, tokens), $"/img/og/{key}-{language}.png");
        seo.JsonLd = StructuredData.ForMarketingPage(await _marketing.FactsAsync(language, _seo.BaseUrl), canonical, title,
            new[] { (t.Home, _seo.Url("/" + SeoLanguages.Query(language, "en"))), (page.Breadcrumb ?? page.H1, canonical) },
            withSoftware: isProduct,
            faq: faqs.Select(f => (Q: LandingContent.Fill(f.Question, tokens), A: PlainAnswer(LandingContent.Fill(f.Answer, tokens))))
                .Where(f => f.A.Length > 0).ToList(),
            pageType: key switch { "about" => "AboutPage", "contact" => "ContactPage", _ => "WebPage" },
            dateModified: LandingContent.LastUpdated.ToString("yyyy-MM-dd"));
        ViewData["Seo"] = seo;
        ViewData["Marketing"] = chrome;
        ViewData["MarketingPage"] = key;

        return View("Page", new LandingPageModel(page, language, tokens, chrome, await _marketing.PricesAsync(),
            key == "faq" ? LandingContent.FaqGroups(language) : null));
    }

    /// <summary>FAQ answers without the [label](/link) markup (the schema gets plain text).</summary>
    private string PlainAnswer(string text) => LandingContent.Plain(text, _seo.BaseUrl);

    /// <summary>
    /// Every restaurant that is happy to be found (SeoIndexing: shown in Google, not a demo, has
    /// dishes), grouped by city, linking to each menu, website and booking page: a crawl path to
    /// every restaurant and a list AI assistants can cite. Plain numbered pages (?page=2).
    /// </summary>
    [HttpGet("restaurants")]
    public async Task<IActionResult> Restaurants(string? lang, int page = 1)
    {
        var registry = MarketingPages.Get("restaurants");
        var (language, wrongLanguage) = MarketingPages.Resolve(registry, lang);
        if (wrongLanguage || page < 1 || Request.Query.ContainsKey("page") && page == 1)
        {
            var query = SeoLanguages.Query(language, "en");
            if (page > 1) query += (query.Length == 0 ? "?" : "&") + "page=" + page;
            return RedirectPermanent("/restaurants" + query);
        }

        var rows = await _db.Branches.AsNoTracking()
            .Where(b => b.ShowInSearch && !b.IsDemo && _db.Products.Any(p => p.BranchId == b.Id && (p.IsAvailable || !b.HideSoldOut)))
            .Select(b => new
            {
                b.Id, b.Name, b.Slug, b.City, b.Cuisine, b.BusinessType, b.Logo, b.SupportedLanguages, b.DefaultLanguage,
                Site = _db.BranchSites.Any(s => s.BranchId == b.Id && s.Enabled),
                Bookings = _db.ReservationSettings.Any(s => s.BranchId == b.Id && s.Enabled)
            })
            .ToListAsync();

        var t = MarketingText.For(language);
        var entries = new List<DirectoryEntry>();
        foreach (var r in rows)
        {
            var plan = await _plans.ForBranchAsync(r.Id);
            var stub = new Branch { Slug = r.Slug, SupportedLanguages = r.SupportedLanguages, DefaultLanguage = r.DefaultLanguage, Name = r.Name, Address = "", PhoneNumber = "", UserId = "" };
            var languages = SeoLanguages.Of(stub);
            var pageLanguage = languages.Contains(language) ? language : SeoLanguages.Default(stub);
            string? site = null;
            if (r.Site && plan?.Has(BillingModule.Website) == true)
                site = (await _hosts.PublicHostAsync(r.Id, r.Slug) is { } host ? $"https://{host}/" : $"/site/{r.Slug}") + SeoLanguages.Query(pageLanguage, SeoLanguages.Default(stub));
            var kind = r.BusinessType == BusinessType.Restaurant && Cuisines.Find(r.Cuisine) == null ? null : Cuisines.Phrase(r.BusinessType, r.Cuisine, language);
            entries.Add(new DirectoryEntry(
                r.Name,
                string.IsNullOrWhiteSpace(r.City) ? null : SeoText.OneLine(r.City),
                kind,
                r.Logo,
                _seo.MenuUrl(stub, pageLanguage),
                site,
                r.Bookings && plan?.Has(BillingModule.Bookings) == true ? _seo.BookUrl(stub, pageLanguage) : null));
        }

        // Cities with the most restaurants first, then alphabetically; restaurants by name.
        var byCity = entries.GroupBy(e => e.City ?? "", StringComparer.OrdinalIgnoreCase).ToList();
        var ordered = byCity
            .OrderBy(g => g.Key.Length == 0).ThenByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(g => g.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)PageSize));
        if (page > pages) return NotFound();
        var shown = ordered.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        var groups = shown.GroupBy(e => e.City ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new DirectoryCity(g.Key.Length == 0 ? t.DirectoryOtherCity : g.First().City!, g.ToList()))
            .ToList();

        string PathFor(string l, int p) => "/restaurants" + SeoLanguages.Query(l, "en") + (p > 1 ? (l == "en" ? "?" : "&") + "page=" + p : "");
        var canonical = _seo.Url(PathFor(language, page));
        var title = page > 1 ? $"{t.DirectoryMetaTitle} ({string.Format(t.PageOf, page, pages)})" : t.DirectoryMetaTitle;
        var seo = Seo(registry, language, title, string.Format(t.DirectoryDescription, ordered.Count), $"/img/og/restaurants-{language}.png");
        seo.CanonicalUrl = canonical;
        seo.Alternates = registry.Languages.Select(l => new SeoAlternate(l, _seo.Url(PathFor(l, page)))).ToList();
        seo.XDefaultUrl = _seo.Url(PathFor("en", page));
        seo.NoIndex = ordered.Count == 0;

        var itemList = new ItemList
        {
            Id = canonical + "#restaurants",
            Name = t.DirectoryTitle,
            NumberOfItems = ordered.Count,
            ItemListElement = shown.Select((e, i) => new ListItem
            {
                Position = (page - 1) * PageSize + i + 1,
                Name = e.Name,
                Item = _seo.Absolute(e.MenuUrl)
            }).ToList()
        };
        seo.JsonLd = StructuredData.ForMarketingPage(await _marketing.FactsAsync(language, _seo.BaseUrl), canonical, title,
            new[] { (t.Home, _seo.Url("/" + SeoLanguages.Query(language, "en"))), (t.DirectoryTitle, canonical) },
            withSoftware: false, pageType: "CollectionPage", extra: new Thing[] { itemList });

        ViewData["Seo"] = seo;
        ViewData["Marketing"] = await _marketing.ChromeAsync(language, "/restaurants");
        ViewData["MarketingPage"] = "restaurants";
        return View(new DirectoryPage(language, groups, ordered.Count, page, pages,
            page > 1 ? PathFor(language, page - 1) : null,
            page < pages ? PathFor(language, page + 1) : null));
    }

    private SeoMetadata Seo(MarketingPages.MarketingPage page, string language, string title, string description, string image) => new()
    {
        Title = title,
        Description = SeoText.Trim(description, SeoText.DescriptionMax),
        CanonicalUrl = _seo.Url(MarketingPages.PathFor(page, language)),
        Alternates = page.Languages.Select(l => new SeoAlternate(l, _seo.Url(MarketingPages.PathFor(page, l)))).ToList(),
        XDefaultUrl = _seo.Url(page.Path),
        Locale = language,
        ImageUrl = _seo.Absolute(image),
        ImageWidth = 1200,
        ImageHeight = 630,
        ImageAlt = title,
        OgType = "website",
        ThemeColor = "#D55C13"
    };
}

public record LandingPageModel(LandingContent.Page Page, string Lang, IReadOnlyDictionary<string, string> Tokens, MarketingChrome Chrome,
    MarketingPrices Prices, IReadOnlyList<(string Group, IReadOnlyList<LandingContent.Faq> Faqs)>? FaqGroups)
{
    public string Fill(string text) => LandingContent.Fill(text, Tokens);
    /// <summary>Filled text as HTML with its links (to this page's language).</summary>
    public Microsoft.AspNetCore.Html.HtmlString Html(string text) => new(LandingContent.Html(Fill(text), Lang));
}

public record DirectoryEntry(string Name, string? City, string? Kind, string? Logo, string MenuUrl, string? SiteUrl, string? BookUrl);
public record DirectoryCity(string City, IReadOnlyList<DirectoryEntry> Restaurants);
public record DirectoryPage(string Lang, IReadOnlyList<DirectoryCity> Cities, int Total, int Page, int Pages, string? PreviousUrl, string? NextUrl);
