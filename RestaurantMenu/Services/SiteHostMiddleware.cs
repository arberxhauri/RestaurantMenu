using System.Net;
using Microsoft.EntityFrameworkCore;

namespace RestaurantMenu.Services;

/// <summary>
/// Requests for a restaurant's own domain (or its subdomain of Sites__WildcardDomain) are
/// served by that restaurant's pages: "/" is its website, "/menu" its menu, "/book" its
/// booking page, and the files and endpoints those pages use pass through. Everything
/// else (back office, other restaurants) goes to the app's own address, so sign-in cookies
/// only ever live there. The app's own hosts are untouched.
/// </summary>
public class SiteHostMiddleware
{
    public const string ItemKey = "SiteTarget";

    private static readonly string[] SharedPrefixes =
    {
        "/css/", "/js/", "/lib/", "/img/", "/images/", "/favicon.ico", "/logo.png", "/sw.js",
        "/menu/event", "/menu/order", "/menu/orders"
    };

    private readonly RequestDelegate _next;
    private readonly string? _appBase;

    public SiteHostMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _appBase = (config["Seo:BaseUrl"] ?? (Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")))?.TrimEnd('/');
    }

    public async Task InvokeAsync(HttpContext context, SiteHosts hosts)
    {
        var host = context.Request.Host.Host.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || hosts.IsAppHost(host))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        var target = await hosts.ResolveAsync(host);

        // The domain check: answers the domain's token so the app knows DNS leads here.
        if (path == DomainService.CheckPath)
        {
            if (target?.Token == null) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = "text/plain";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(target.Token);
            return;
        }

        if (target == null)
        {
            // www.example.al when only example.al is connected (or the other way round).
            var other = host.StartsWith("www.") ? host[4..] : "www." + host;
            if (await hosts.ResolveAsync(other) is { Verified: true } alt)
            {
                context.Response.Redirect($"https://{alt.Host}{context.Request.PathBase}{path}{context.Request.QueryString}", permanent: true);
                return;
            }
            await Message(context, 404, "This address isn't connected to a restaurant",
                "If it's yours, add it on the Website page of your branch in My Quick Menu.");
            return;
        }
        if (!target.Verified)
        {
            await Message(context, 404, "This website is being connected",
                "The domain was added and is waiting for its DNS record. It's usually live within a few hours.");
            return;
        }
        if (!target.Published)
        {
            await Message(context, 404, "This website isn't published yet", "Check back soon.");
            return;
        }

        var slug = target.Slug;
        string? rewrite = path switch
        {
            "/" or "" => $"/site/{slug}",
            "/menu" or "/menu/" => $"/menu/{slug}",
            "/book" or "/book/" => $"/book/{slug}",
            "/robots.txt" => $"/site/{slug}/robots.txt",
            "/sitemap.xml" => $"/site/{slug}/sitemap.xml",
            _ => null
        };
        // Whole segments only: /menu/{slug}x is another restaurant.
        bool Under(string prefix) => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                                     || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        var own = Under($"/site/{slug}") || Under($"/menu/{slug}") || Under($"/book/{slug}")
                  || SharedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (rewrite == null && !own)
        {
            // Back office, sign-in, other restaurants: on the app's own address.
            if (_appBase != null) context.Response.Redirect($"{_appBase}{path}{context.Request.QueryString}");
            else context.Response.StatusCode = 404;
            return;
        }

        context.Items[ItemKey] = target;
        if (rewrite != null) context.Request.Path = rewrite;
        await _next(context);
    }

    private static async Task Message(HttpContext context, int status, string title, string text)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync($$"""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="robots" content="noindex"><title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#F4F4F2;color:#161616;font:16px/1.5 system-ui,-apple-system,sans-serif;padding:24px}
            main{max-width:440px;text-align:center}h1{font-size:24px;margin:0 0 8px}p{color:#4A4A47;margin:0}</style></head>
            <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(text)}}</p></main></body></html>
            """);
    }
}
