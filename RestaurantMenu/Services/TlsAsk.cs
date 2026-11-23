using System.Net;
using System.Net.Sockets;
using RestaurantMenu.Helpers;

namespace RestaurantMenu.Services;

/// <summary>
/// Who may get an HTTPS certificate on demand. Caddy (in front of the app) has fixed certificates
/// for the app's own hosts; for restaurants' own domains and their subdomains of
/// Sites__WildcardDomain it gets one on the first visit, after asking <see cref="TlsAskMiddleware"/>.
/// </summary>
public static class TlsAskRules
{
    /// <summary>
    /// Only Caddy asks, from the server itself or the Docker network. A visitor's request through
    /// Caddy carries X-Forwarded-For, so after UseForwardedHeaders it shows the visitor's public address.
    /// </summary>
    public static bool IsTrustedCaller(IPAddress? address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        var b = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168),
            AddressFamily.InterNetworkV6 => (b[0] & 0xFE) == 0xFC, // fc00::/7, unique local
            _ => false
        };
    }

    /// <summary>The asked host in the form domains are stored in, or null if it can't be a restaurant's.</summary>
    public static string? Candidate(string? domain) => SiteRules.NormalizeHost(domain);

    /// <summary>www.example.al ↔ example.al.</summary>
    public static string Twin(string host) => host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : "www." + host;

    /// <summary>
    /// A connected host (verified or not: the domain check comes in over https after Caddy's redirect),
    /// or the twin of a verified one, which SiteHostMiddleware redirects to it.
    /// </summary>
    public static bool Allows(SiteTarget? exact, SiteTarget? twin) => exact != null || twin is { Verified: true };
}

/// <summary>
/// GET /internal/tls-ask?domain=… → 200 when Caddy may get a certificate for the domain, else 404.
/// Runs before SiteHostMiddleware: Caddy calls it as http://mqm:8080, which is no app host.
/// Caddy also refuses /internal/* from outside.
/// </summary>
public sealed class TlsAskMiddleware
{
    public const string Path = "/internal/tls-ask";

    private readonly RequestDelegate _next;
    private readonly ILogger<TlsAskMiddleware> _logger;

    public TlsAskMiddleware(RequestDelegate next, ILogger<TlsAskMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SiteHosts hosts)
    {
        if (!context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        if (!TlsAskRules.IsTrustedCaller(context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var host = TlsAskRules.Candidate(context.Request.Query["domain"]);
        if (host == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var allowed = TlsAskRules.Allows(await hosts.ResolveAsync(host), await hosts.ResolveAsync(TlsAskRules.Twin(host)));
        // Scanners send random names all day, so refusals are only worth a debug line.
        if (allowed) _logger.LogInformation("TLS: certificate allowed for {Host}", host);
        else _logger.LogDebug("TLS: certificate refused for {Host}", host);
        context.Response.StatusCode = allowed ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;
    }
}
