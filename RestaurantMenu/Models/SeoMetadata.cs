namespace RestaurantMenu.Models;

/// <summary>
/// Everything the &lt;head&gt; needs to describe one page to crawlers and social
/// scrapers. Built in the controller and rendered by Views/Shared/_SeoMeta.cshtml.
/// </summary>
public class SeoMetadata
{
    /// <summary>Full &lt;title&gt;. Keep under ~60 characters so Google does not truncate it.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Meta description. Aim for 120-160 characters.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Absolute self-referencing canonical URL.</summary>
    public string CanonicalUrl { get; set; } = string.Empty;

    /// <summary>Absolute URL of the social preview image.</summary>
    public string? ImageUrl { get; set; }

    public string? ImageAlt { get; set; }

    /// <summary>Open Graph type: "website", "article", "restaurant.menu", ...</summary>
    public string OgType { get; set; } = "website";

    /// <summary>When true the page asks to be excluded from every index.</summary>
    public bool NoIndex { get; set; }

    /// <summary>Two-letter language of this page, used for og:locale and html lang.</summary>
    public string Locale { get; set; } = "en";

    /// <summary>hreflang alternates. Empty for single-language pages.</summary>
    public IReadOnlyList<SeoAlternate> Alternates { get; set; } = Array.Empty<SeoAlternate>();

    /// <summary>
    /// Pre-serialised JSON-LD written into a ld+json script tag. Must be produced
    /// by System.Text.Json with the default (HTML-escaping) encoder.
    /// </summary>
    public string? JsonLd { get; set; }

    /// <summary>Browser UI colour, matched to the page's own palette.</summary>
    public string? ThemeColor { get; set; }

    /// <summary>Favicon for this page. Branch menus override it with the restaurant's own logo.</summary>
    public string FaviconUrl { get; set; } = "/logo.png";
}

/// <summary>One hreflang alternate of a page.</summary>
public record SeoAlternate(string Language, string Url);
