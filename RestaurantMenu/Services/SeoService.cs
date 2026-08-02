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
    /// The public menu URL for a branch. <paramref name="language"/> is appended only
    /// when it is a non-default language, so the English page stays on the bare URL
    /// rather than competing with an identical ?lang=en duplicate.
    /// </summary>
    public string MenuUrl(string branchName, string? language = null)
    {
        var url = $"{BaseUrl}/menu/{Uri.EscapeDataString(Slug(branchName))}";
        return string.IsNullOrEmpty(language) || language == DefaultLanguage
            ? url
            : $"{url}?lang={Uri.EscapeDataString(language)}";
    }

    public const string DefaultLanguage = "en";

    /// <summary>
    /// The branch's URL segment. MenuController compares names case-insensitively with
    /// spaces stripped, so lowercasing here gives one canonical spelling per branch
    /// instead of one per capitalisation.
    /// </summary>
    public static string Slug(string branchName) =>
        branchName.Replace(" ", string.Empty).ToLowerInvariant();
}
