namespace RestaurantMenu.Models;

/// <summary>
/// Everything the &lt;head&gt; needs to describe one page to crawlers, social scrapers and AI
/// assistants. Built in the controller (ViewData["Seo"]) and rendered by
/// Views/Shared/_SeoMeta.cshtml; the layout takes &lt;html lang&gt; from <see cref="Locale"/>.
/// How to add SEO to a new page: docs/SEO-GEO.md.
/// </summary>
public class SeoMetadata
{
    /// <summary>Full &lt;title&gt;. Keep under ~60 characters so Google does not truncate it.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Meta description, 140-160 characters (SeoText.Trim cuts at a word).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Absolute self-referencing canonical URL, including ?lang= for a non-main language.</summary>
    public string CanonicalUrl { get; set; } = string.Empty;

    /// <summary>Absolute URL of the social preview image (ideally 1200×630).</summary>
    public string? ImageUrl { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public string? ImageAlt { get; set; }

    /// <summary>og:site_name: the restaurant's name on its own pages.</summary>
    public string SiteName { get; set; } = "My Quick Menu";

    /// <summary>Open Graph type: "website", "article", "restaurant.menu", ...</summary>
    public string OgType { get; set; } = "website";

    /// <summary>Asks to be left out of search results ("noindex").</summary>
    public bool NoIndex { get; set; }

    /// <summary>With <see cref="NoIndex"/>: also don't follow links (back-office screens). Public pages keep "follow".</summary>
    public bool NoFollow { get; set; }

    /// <summary>Two-letter language of this page: html lang, og:locale (as xx_YY) and JSON-LD inLanguage.</summary>
    public string Locale { get; set; } = "en";

    /// <summary>hreflang alternates, this page's own language included. Empty for single-language pages.</summary>
    public IReadOnlyList<SeoAlternate> Alternates { get; set; } = Array.Empty<SeoAlternate>();

    /// <summary>hreflang="x-default": the bare URL in the page's main language. Defaults to the first alternate.</summary>
    public string? XDefaultUrl { get; set; }

    /// <summary>
    /// Pre-serialised JSON-LD written into a ld+json script tag. Must be produced
    /// by JsonLd.Serialize (System.Text.Json with an HTML-safe encoder).
    /// </summary>
    public string? JsonLd { get; set; }

    /// <summary>Browser UI colour, matched to the page's own palette.</summary>
    public string? ThemeColor { get; set; }

    /// <summary>Favicon for this page. Branch menus override it with the restaurant's own logo.</summary>
    public string FaviconUrl { get; set; } = "/logo.png";

    /// <summary>The page's data last changed (sent as Last-Modified by HtmlCacheHeaders).</summary>
    public DateTime? LastModifiedUtc { get; set; }

    /// <summary>The robots meta value for this page.</summary>
    public string RobotsContent => NoIndex
        ? NoFollow ? "noindex, nofollow" : "noindex, follow"
        : "index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1";
}

/// <summary>One hreflang alternate of a page.</summary>
public record SeoAlternate(string Language, string Url);
