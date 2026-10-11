using Microsoft.AspNetCore.Http;

namespace RestaurantMenu.Helpers;

/// <summary>Which AI assistant sent a visitor, from the Referer header or utm_source. Pure; tested.</summary>
public static class AiReferrals
{
    private static readonly (string Host, string Source)[] Hosts =
    {
        ("chatgpt.com", "ChatGPT"), ("chat.openai.com", "ChatGPT"),
        ("perplexity.ai", "Perplexity"),
        ("claude.ai", "Claude"),
        ("gemini.google.com", "Gemini"),
        ("copilot.microsoft.com", "Copilot")
    };

    /// <summary>The assistant and how it was recognised, or null for every other visit.</summary>
    public static (string Source, string Via)? Detect(string? referer, string? utmSource)
    {
        if (!string.IsNullOrWhiteSpace(utmSource))
        {
            var utm = utmSource.Trim().ToLowerInvariant();
            foreach (var (host, source) in Hosts)
                if (utm == host || utm == host.Split('.')[0] || utm.EndsWith("." + host))
                    return (source, "utm_source=" + utm[..Math.Min(utm.Length, 40)]);
        }
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        {
            var h = uri.Host.ToLowerInvariant();
            foreach (var (host, source) in Hosts)
                if (h == host || h.EndsWith("." + host)) return (source, h);
        }
        return null;
    }

    /// <summary>Public pages only: not the back office, files or API calls.</summary>
    public static bool IsPublicPage(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var path = request.Path.Value ?? "/";
        if (Path.HasExtension(path) && !path.EndsWith(".txt")) return false;
        // "/admin/" also covers "/admin" itself.
        return !RobotsTxt.AppOnlyPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                                                 || path.Equals(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
               && !path.StartsWith("/menu/event", StringComparison.OrdinalIgnoreCase)
               && !path.StartsWith("/menu/order", StringComparison.OrdinalIgnoreCase);
    }
}
