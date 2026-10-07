namespace RestaurantMenu.Services;

/// <summary>
/// Security headers on every response:
/// - X-Content-Type-Options: nosniff and Referrer-Policy: strict-origin-when-cross-origin;
/// - no framing (X-Frame-Options DENY, CSP frame-ancestors 'none'), except the guest pages a
///   restaurant may embed in its own site: /menu, /site and /book;
/// - on /billing (where money is handled) a full Content-Security-Policy: only this site and
///   Paddle (the card checkout) may run scripts, frames or connections. The billing pages load
///   their icons and fonts from this site (_HeadAssets with NoThirdParty), so nothing else is needed.
/// The path is read when the response starts, after SiteHostMiddleware's rewrites.
/// </summary>
public class SecurityHeaders
{
    private const string Paddle = "https://*.paddle.com https://paddle.com";

    public static readonly string BillingPolicy = string.Join("; ",
        "default-src 'self'",
        $"script-src 'self' {Paddle}",
        $"style-src 'self' 'unsafe-inline' {Paddle}",
        $"img-src 'self' data: {Paddle}",
        $"font-src 'self' {Paddle}",
        $"connect-src 'self' {Paddle}",
        $"frame-src {Paddle}",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "object-src 'none'",
        // The customer portal and checkout are reached by a redirect after our own form posts.
        $"form-action 'self' {Paddle}");

    private static readonly string[] Embeddable = { "/menu", "/site", "/book" };

    private readonly RequestDelegate _next;

    public SecurityHeaders(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            var path = context.Request.Path;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (path.StartsWithSegments("/billing") && !path.StartsWithSegments("/billing/webhooks"))
            {
                h["Content-Security-Policy"] = BillingPolicy;
                h["X-Frame-Options"] = "DENY";
            }
            else if (!Embeddable.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                h["X-Frame-Options"] = "DENY";
                if (!h.ContainsKey("Content-Security-Policy")) h["Content-Security-Policy"] = "frame-ancestors 'none'";
            }
            return Task.CompletedTask;
        });
        return _next(context);
    }
}
