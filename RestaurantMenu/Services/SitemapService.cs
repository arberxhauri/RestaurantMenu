using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// The app host's sitemaps: /sitemap.xml is an index of
///   /sitemaps/pages.xml  marketing pages in each language,
///   /sitemaps/menus.xml  every indexable menu in each of its languages, with dish photos (split
///                        into menus-2.xml, menus-3.xml… past <see cref="MaxUrls"/> URLs),
///   /sitemaps/sites.xml  published websites that have no address of their own (those list
///                        themselves at their own domain's /sitemap.xml).
/// Only pages that are indexable go in (SeoIndexing). Built from the database at most once an
/// hour, and again as soon as a restaurant saves something (SeoChanges).
/// </summary>
public class SitemapService
{
    /// <summary>The protocol allows 50,000; a margin for the language alternates' growth.</summary>
    public const int MaxUrls = 45_000;
    /// <summary>Google reads at most 1,000 images per page.</summary>
    private const int MaxImagesPerUrl = 1000;

    public static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    public static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    public static readonly XNamespace Image = "http://www.google.com/schemas/sitemap-image/1.1";

    private readonly ApplicationDbContext _db;
    private readonly SeoService _seo;
    private readonly SiteHosts _hosts;
    private readonly IEntitlementService _plans;
    private readonly IMemoryCache _cache;
    private readonly SeoChanges _changes;

    public SitemapService(ApplicationDbContext db, SeoService seo, SiteHosts hosts, IEntitlementService plans, IMemoryCache cache, SeoChanges changes)
    {
        _db = db;
        _seo = seo;
        _hosts = hosts;
        _plans = plans;
        _cache = cache;
        _changes = changes;
    }

    /// <summary>One menu or site URL with its language alternates.</summary>
    public record Entry(string Url, IReadOnlyList<SeoAlternate> Alternates, string? XDefault, DateTime? LastModified, IReadOnlyList<string> Images);

    // ------------------------------------------------------------------ data (cached)

    private Task<T> Cached<T>(string key, Func<Task<T>> build) =>
        _cache.GetOrCreateAsync($"sitemap:{_seo.BaseUrl}:{key}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            e.AddExpirationToken(_changes.Token);
            return build();
        })!;

    /// <summary>Every indexable menu in every language. Also used by llms.txt and the directory.</summary>
    public Task<List<Entry>> MenuEntriesAsync() => Cached("menus", async () =>
    {
        var branches = await _db.Branches.AsNoTracking()
            .Where(b => b.ShowInSearch && !b.IsDemo)
            .Select(b => new
            {
                Branch = b,
                Dishes = _db.Products.Where(p => p.BranchId == b.Id && (p.IsAvailable || !b.HideSoldOut)).Count(),
                Photos = _db.Products.Where(p => p.BranchId == b.Id && p.Image != null && p.Image != "" && (p.IsAvailable || !b.HideSoldOut))
                    .OrderBy(p => p.DisplayOrder).Select(p => p.Image!).Take(MaxImagesPerUrl).ToList(),
                Categories = _db.Categories.IgnoreQueryFilters().Where(c => c.BranchId == b.Id).Max(c => (DateTime?)c.UpdatedUtc),
                Products = _db.Products.IgnoreQueryFilters().Where(p => p.BranchId == b.Id).Max(p => (DateTime?)p.UpdatedUtc)
            })
            .OrderBy(x => x.Branch.Slug)
            .ToListAsync();

        var entries = new List<Entry>();
        foreach (var x in branches.Where(x => SeoIndexing.Menu(x.Branch, x.Dishes)))
        {
            var b = x.Branch;
            var languages = SeoLanguages.Of(b);
            var alternates = languages.Select(l => new SeoAlternate(l, _seo.MenuUrl(b, l))).ToList();
            var lastModified = new[] { b.UpdatedUtc, x.Categories ?? DateTime.MinValue, x.Products ?? DateTime.MinValue }.Max();
            var images = new List<string>();
            if (!string.IsNullOrWhiteSpace(b.Banner)) images.Add(_seo.Absolute(b.Banner));
            images.AddRange(x.Photos.Select(_seo.Absolute));
            foreach (var language in languages)
            {
                // The photos once per menu, on its main-language URL: the same images on every
                // translation only repeat themselves.
                var main = language == SeoLanguages.Default(b);
                entries.Add(new Entry(_seo.MenuUrl(b, language), alternates, _seo.MenuUrl(b), lastModified,
                    main ? images.Take(MaxImagesPerUrl).ToList() : Array.Empty<string>()));
            }
        }
        return entries;
    });

    /// <summary>Published, indexable websites served from /site/{slug} on this host, in every language.</summary>
    public Task<List<Entry>> SiteEntriesAsync() => Cached("sites", async () =>
    {
        var sites = await _db.BranchSites.AsNoTracking()
            .Where(s => s.Enabled && s.Branch!.ShowInSearch && !s.Branch.IsDemo)
            .Select(s => new { s.Branch, s.UpdatedUtc })
            .ToListAsync();

        var entries = new List<Entry>();
        foreach (var s in sites.OrderBy(s => s.Branch!.Slug))
        {
            var b = s.Branch!;
            if ((await _plans.ForBranchAsync(b.Id))?.Has(BillingModule.Website) != true) continue;
            // An own domain or free subdomain lists the site in its own sitemap.
            if (await _hosts.PublicHostAsync(b.Id, b.Slug) != null) continue;
            var languages = SeoLanguages.Of(b);
            var main = SeoLanguages.Default(b);
            string Url(string l) => _seo.Url($"/site/{b.Slug}") + SeoLanguages.Query(l, main);
            var alternates = languages.Select(l => new SeoAlternate(l, Url(l))).ToList();
            var lastModified = new[] { b.UpdatedUtc, s.UpdatedUtc }.Max();
            var images = string.IsNullOrWhiteSpace(b.Banner) ? Array.Empty<string>() : new[] { _seo.Absolute(b.Banner) };
            foreach (var l in languages)
                entries.Add(new Entry(Url(l), alternates, Url(main), lastModified, l == main ? images : Array.Empty<string>()));
        }
        return entries;
    });

    /// <summary>The marketing pages in each of their languages.</summary>
    public List<Entry> PageEntries()
    {
        var entries = new List<Entry>();
        foreach (var page in MarketingPages.All)
        {
            var alternates = page.Languages.Select(l => new SeoAlternate(l, _seo.Url(MarketingPages.PathFor(page, l)))).ToList();
            foreach (var l in page.Languages)
                entries.Add(new Entry(_seo.Url(MarketingPages.PathFor(page, l)), alternates, _seo.Url(page.Path), null, Array.Empty<string>()));
        }
        return entries;
    }

    // ------------------------------------------------------------------ XML

    /// <summary>The number of menu sitemap files (at least 1).</summary>
    public async Task<int> MenuFileCountAsync() => Math.Max(1, (int)Math.Ceiling((await MenuEntriesAsync()).Count / (double)MaxUrls));

    public async Task<XDocument> IndexAsync()
    {
        var menus = await MenuEntriesAsync();
        var sites = await SiteEntriesAsync();
        var files = new List<(string Path, DateTime? LastModified)> { ("/sitemaps/pages.xml", null) };
        var menuFiles = await MenuFileCountAsync();
        for (var n = 1; n <= menuFiles; n++)
        {
            var chunk = menus.Skip((n - 1) * MaxUrls).Take(MaxUrls).ToList();
            files.Add((n == 1 ? "/sitemaps/menus.xml" : $"/sitemaps/menus-{n}.xml", chunk.Count == 0 ? null : chunk.Max(e => e.LastModified)));
        }
        files.Add(("/sitemaps/sites.xml", sites.Count == 0 ? null : sites.Max(e => e.LastModified)));

        var root = new XElement(Ns + "sitemapindex");
        foreach (var (path, lastModified) in files)
        {
            var el = new XElement(Ns + "sitemap", new XElement(Ns + "loc", _seo.Url(path)));
            if (lastModified is { } lm) el.Add(new XElement(Ns + "lastmod", W3c(lm)));
            root.Add(el);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    /// <summary>A urlset for one sitemap file; null for a file that doesn't exist (e.g. menus-9.xml).</summary>
    public async Task<XDocument?> FileAsync(string name, int part = 1)
    {
        List<Entry> entries;
        string changeFrequency;
        switch (name)
        {
            case "pages":
                entries = PageEntries();
                changeFrequency = "weekly";
                break;
            case "menus":
                if (part < 1 || part > await MenuFileCountAsync()) return null;
                entries = (await MenuEntriesAsync()).Skip((part - 1) * MaxUrls).Take(MaxUrls).ToList();
                changeFrequency = "weekly";
                break;
            case "sites":
                entries = await SiteEntriesAsync();
                changeFrequency = "weekly";
                break;
            default:
                return null;
        }
        return UrlSet(entries, changeFrequency);
    }

    /// <summary>A urlset (also used for a restaurant's own domain).</summary>
    public static XDocument UrlSet(IEnumerable<Entry> entries, string changeFrequency)
    {
        var root = new XElement(Ns + "urlset",
            new XAttribute(XNamespace.Xmlns + "xhtml", Xhtml),
            new XAttribute(XNamespace.Xmlns + "image", Image));
        foreach (var e in entries)
        {
            var url = new XElement(Ns + "url", new XElement(Ns + "loc", e.Url));
            if (e.LastModified is { } lm && lm > DateTime.MinValue) url.Add(new XElement(Ns + "lastmod", W3c(lm)));
            url.Add(new XElement(Ns + "changefreq", changeFrequency));
            // Every translation names all of them (itself included) and the x-default, so the
            // annotations are reciprocal, which is what Google requires.
            if (e.Alternates.Count > 1)
            {
                foreach (var a in e.Alternates)
                    url.Add(new XElement(Xhtml + "link", new XAttribute("rel", "alternate"), new XAttribute("hreflang", a.Language), new XAttribute("href", a.Url)));
                if (e.XDefault != null)
                    url.Add(new XElement(Xhtml + "link", new XAttribute("rel", "alternate"), new XAttribute("hreflang", "x-default"), new XAttribute("href", e.XDefault)));
            }
            foreach (var img in e.Images)
                url.Add(new XElement(Image + "image", new XElement(Image + "loc", img)));
            root.Add(url);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static string W3c(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
}
