using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace RestaurantMenu.Services;

/// <summary>
/// One spelling per page (docs/SEO-GEO.md, "URL hygiene"): GET requests for
///  - a path with a trailing slash ("/menu/oliva/") → without it,
///  - a path with capitals ("/Menu/Oliva") → lower case (uploaded files and assets excepted:
///    their names are case-sensitive on disk),
///  - the Render hostname (*.onrender.com) → Seo:BaseUrl, when Seo:RedirectRenderHost is true,
/// are answered with a 301. Runs before SiteHostMiddleware, on the address the guest typed.
/// HTTPS and www → apex are handled in front of the app (Render).
/// </summary>
public class UrlHygieneMiddleware
{
    private static readonly string[] CaseSensitive = { "/images/", "/img/", "/css/", "/js/", "/lib/", "/hubs/", "/.well-known/" };

    private readonly RequestDelegate _next;
    private readonly string? _baseUrl;
    private readonly bool _redirectRenderHost;

    public UrlHygieneMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _baseUrl = config["Seo:BaseUrl"]?.TrimEnd('/');
        _redirectRenderHost = config.GetValue("Seo:RedirectRenderHost", false);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return _next(context);

        var path = request.Path.Value ?? "/";
        var target = path;
        if (target.Length > 1 && target.EndsWith('/')) target = target.TrimEnd('/');
        if (target.Length == 0) target = "/";
        var hasExtension = Path.HasExtension(target);
        if (!hasExtension && !CaseSensitive.Any(p => target.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && target.Any(char.IsUpper))
            target = target.ToLowerInvariant();

        var host = request.Host.Host;
        var toBase = _redirectRenderHost && _baseUrl != null && host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase);

        if (target == path && !toBase) return _next(context);

        var location = (toBase ? _baseUrl : "") + request.PathBase + target + request.QueryString;
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = location;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Validators for public HTML (menus, websites, marketing pages): a weak ETag from the page's
/// bytes and Last-Modified from its data (SeoMetadata.LastModifiedUtc), with
/// "Cache-Control: no-cache" so browsers and crawlers revalidate and get a 304 when nothing
/// changed. The ETag is taken from the rendered page rather than the data because a menu also
/// shows the time of day ("Open until 23:00"). Pages that set a cookie are left alone.
/// </summary>
public partial class HtmlValidatorsMiddleware
{
    public const string LastModifiedKey = "Seo.LastModified";
    private static readonly string[] Prefixes = { "/menu/", "/site/" };
    private const int MaxBuffered = 4 * 1024 * 1024;

    private readonly RequestDelegate _next;

    public HtmlValidatorsMiddleware(RequestDelegate next) => _next = next;

    /// <summary>
    /// The page's weak ETag. The menu's data-rendered-at (when this copy was made, for the offline
    /// notice) is left out: it changes on every request without the page changing.
    /// </summary>
    public static string ETagFor(ReadOnlySpan<byte> html)
    {
        var text = RenderedAt().Replace(Encoding.UTF8.GetString(html), "");
        return $"W/\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32].ToLowerInvariant()}\"";
    }

    [GeneratedRegex("data-rendered-at=\"[^\"]*\"")]
    private static partial Regex RenderedAt();

    private static bool Applies(HttpRequest r)
    {
        if (!HttpMethods.IsGet(r.Method) && !HttpMethods.IsHead(r.Method)) return false;
        var path = r.Path.Value ?? "/";
        if (path.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase)) return false;
        return Prefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
               || Helpers.MarketingPages.All.Any(p => p.Path == path);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Applies(context.Request))
        {
            await _next(context);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        var response = context.Response;
        var html = response.StatusCode == 200 && (response.ContentType ?? "").StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        if (html && !response.Headers.ContainsKey(HeaderNames.SetCookie) && buffer.Length <= MaxBuffered)
        {
            var etag = ETagFor(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            response.Headers.ETag = etag;
            if (!response.Headers.ContainsKey(HeaderNames.CacheControl)) response.Headers.CacheControl = "no-cache";
            if (context.Items[LastModifiedKey] is DateTime lm)
                response.Headers.LastModified = DateTime.SpecifyKind(lm, DateTimeKind.Utc).ToString("R");

            var match = context.Request.Headers.IfNoneMatch.ToString();
            if (match.Length > 0 && match.Split(',').Select(x => x.Trim()).Any(x => x == etag || x == "*" || x == etag[2..]))
            {
                response.StatusCode = StatusCodes.Status304NotModified;
                response.ContentLength = null;
                response.Headers.Remove(HeaderNames.ContentLength);
                return;
            }
        }

        buffer.Position = 0;
        if (!HttpMethods.IsHead(context.Request.Method)) await buffer.CopyToAsync(original);
    }
}
