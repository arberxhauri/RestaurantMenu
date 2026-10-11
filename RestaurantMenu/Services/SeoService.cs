using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Builds the absolute URLs that canonical tags, hreflang alternates, Open Graph
/// tags, the sitemap and JSON-LD all have to agree on.
/// </summary>
public class SeoService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly string? _configuredBaseUrl;

    public SeoService(IHttpContextAccessor accessor, IConfiguration configuration)
    {
        _accessor = accessor;
        _configuredBaseUrl = configuration["Seo:BaseUrl"]?.TrimEnd('/');
        _wildcard = SiteRules.NormalizeHost(configuration["Sites:WildcardDomain"]);
        _subdomainHome = _wildcard != null && configuration.GetValue("Sites:SubdomainIsHome", true);
    }

    public string SiteName => "My Quick Menu";

    /// <summary>
    /// Scheme + host every absolute URL is built from. A configured Seo:BaseUrl wins
    /// over the request host, so once a custom domain sits in front of the app the
    /// onrender.com hostname can never leak into a canonical tag and split ranking
    /// signals across two hostnames.
    /// </summary>
    public string BaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_configuredBaseUrl))
            {
                return _configuredBaseUrl;
            }

            var request = _accessor.HttpContext?.Request;
            return request == null ? string.Empty : $"{request.Scheme}://{request.Host.Value}";
        }
    }

    /// <summary>Turns a stored path ("/images/x.jpg") or a full URL into an absolute URL.</summary>
    public string Absolute(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return string.Empty;
        }

        // Only http(s) counts as already-absolute. Without the scheme check a stored
        // path like "/images/logo.png" parses as an absolute file:// URI on Unix and
        // would be emitted verbatim into og:image, which no crawler can fetch.
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        return $"{BaseUrl}/{pathOrUrl.TrimStart('~', '/')}";
    }

    /// <summary>Absolute URL for an application-relative path such as "/" or "/home/privacy".</summary>
    public string Url(string relativePath)
    {
        var trimmed = relativePath.TrimStart('~', '/');
        return trimmed.Length == 0 ? $"{BaseUrl}/" : $"{BaseUrl}/{trimmed}";
    }

    /// <summary>
    /// The public menu URL for a branch. <paramref name="language"/> is appended only when it isn't
    /// the branch's main language (SeoLanguages.Default), so the main language stays on the bare URL
    /// rather than competing with an identical ?lang= duplicate.
    /// <paramref name="table"/> (?t=) is the table a QR code was printed for, and <paramref name="code"/>
    /// (&amp;k=) its ordering code. They never go into canonical or hreflang URLs.
    /// </summary>
    public string MenuUrl(Branch branch, string? language = null, int? table = null, string? code = null) =>
        MenuUrl(branch.Slug, language, table, code, SeoLanguages.Default(branch));

    private readonly string? _wildcard;
    private readonly bool _subdomainHome;

    /// <summary>
    /// The restaurant's own subdomain ("oliva.myquickmenu.com") when it is its main address: with
    /// Sites__WildcardDomain set, and unless Sites__SubdomainIsHome=false (the off switch if the
    /// subdomains ever stop working). Then its menu is https://{host}/menu and its booking page
    /// https://{host}/book, and the myquickmenu.com/menu/{slug} address that printed QR codes carry
    /// forwards there (301). Null: everything stays on the app's own address.
    /// </summary>
    public string? RestaurantHost(string slug) =>
        _subdomainHome && SiteHosts.Label(slug) is { } label ? $"{label}.{_wildcard}" : null;

    /// <summary>
    /// The address printed on QR codes: always myquickmenu.com/menu/{slug}, which never depends on
    /// DNS and keeps working forever; it forwards to the restaurant's main address.
    /// </summary>
    public string QrMenuUrl(Branch branch, int? table = null, string? code = null)
    {
        var url = $"{BaseUrl}/menu/{Uri.EscapeDataString(branch.Slug)}";
        if (table == null) return url;
        return $"{url}?t={table}" + (string.IsNullOrEmpty(code) ? "" : $"&k={Uri.EscapeDataString(code)}");
    }

    /// <summary>The booking page in a language, on the restaurant's main address.</summary>
    public string BookUrl(Branch branch, string? language = null)
    {
        var query = language == null ? "" : SeoLanguages.Query(language, SeoLanguages.Default(branch));
        return RestaurantHost(branch.Slug) is { } host ? $"https://{host}/book{query}" : $"{BaseUrl}/book/{Uri.EscapeDataString(branch.Slug)}{query}";
    }

    /// <param name="slug">The branch's stored link (Branch.Slug).</param>
    /// <param name="defaultLanguage">The branch's main language; English when not given.</param>
    public string MenuUrl(string slug, string? language = null, int? table = null, string? code = null, string? defaultLanguage = null)
    {
        var url = RestaurantHost(slug) is { } host ? $"https://{host}/menu" : $"{BaseUrl}/menu/{Uri.EscapeDataString(slug)}";
        var query = new List<string>(3);
        if (!string.IsNullOrEmpty(language) && language != (defaultLanguage ?? DefaultLanguage))
        {
            query.Add($"lang={Uri.EscapeDataString(language)}");
        }
        if (table != null)
        {
            query.Add($"t={table}");
            if (!string.IsNullOrEmpty(code))
            {
                query.Add($"k={Uri.EscapeDataString(code)}");
            }
        }
        return query.Count == 0 ? url : $"{url}?{string.Join("&", query)}";
    }

    /// <summary>The marketing site's main language, and the fallback for a branch without one.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Highest table number a menu link or QR code accepts.</summary>
    public const int MaxTable = 9999;

    /// <summary>The table number if it is one a QR code could carry, otherwise null.</summary>
    public static int? ValidTable(int? table) => table is >= 1 and <= MaxTable ? table : null;
}
