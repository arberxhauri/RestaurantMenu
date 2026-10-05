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
    /// <paramref name="table"/> (?t=) is the table a QR code was printed for. It never
    /// goes into canonical or hreflang URLs, which always call this without it.
    /// </summary>
    /// <paramref name="code"/> (&amp;k=) is the table's ordering code, only with a table.
    /// <paramref name="slug"/> is the branch's stored link (Branch.Slug).
    public string MenuUrl(string slug, string? language = null, int? table = null, string? code = null)
    {
        var url = $"{BaseUrl}/menu/{Uri.EscapeDataString(slug)}";
        var query = new List<string>(2);
        if (!string.IsNullOrEmpty(language) && language != DefaultLanguage)
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

    public const string DefaultLanguage = "en";

    /// <summary>Highest table number a menu link or QR code accepts.</summary>
    public const int MaxTable = 9999;

    /// <summary>The table number if it is one a QR code could carry, otherwise null.</summary>
    public static int? ValidTable(int? table) => table is >= 1 and <= MaxTable ? table : null;
}
