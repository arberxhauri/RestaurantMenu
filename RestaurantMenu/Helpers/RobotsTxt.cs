using System.Text;

namespace RestaurantMenu.Helpers;

/// <summary>
/// robots.txt for the app's own host and for restaurants' own domains. AI search and assistant
/// crawlers are welcomed by name: being read by them is how restaurants and My Quick Menu get
/// cited in ChatGPT, Perplexity, Claude, Gemini and Copilot answers. Tokens checked against each
/// vendor's documentation on 2026-10-11 (OpenAI, Perplexity, Anthropic, Google, Apple, Microsoft);
/// see docs/SEO-GEO.md before adding or removing one.
/// </summary>
public static class RobotsTxt
{
    /// <summary>Back-office areas: behind sign-in anyway, never worth a crawler's time.</summary>
    public static readonly IReadOnlyList<string> AppOnlyPaths = new[]
    {
        "/account/", "/admin/", "/billing/", "/branch/", "/category/", "/dashboard/", "/kitchen/",
        "/manage", "/product/", "/team/", "/translate/", "/hubs/", "/home/error", "/home/status/"
    };

    /// <summary>
    /// Crawlers named in their own group. A crawler obeys only the most specific group that names
    /// it, so this group repeats the disallowed paths rather than inheriting them from "*".
    /// </summary>
    public static readonly IReadOnlyList<string> AiAgents = new[]
    {
        "GPTBot", "OAI-SearchBot", "ChatGPT-User",          // OpenAI
        "PerplexityBot", "Perplexity-User",                 // Perplexity
        "ClaudeBot", "Claude-SearchBot", "Claude-User",     // Anthropic
        "Google-Extended",                                  // Gemini (a control token, not a crawler)
        "Applebot", "Applebot-Extended",                    // Apple Intelligence, Siri, Spotlight
        "Bingbot"                                           // Bing, which feeds ChatGPT search and Copilot
    };

    public static string Build(string sitemapUrl, IReadOnlyList<string> disallow)
    {
        var sb = new StringBuilder();
        sb.Append("# My Quick Menu: restaurant menus, websites and bookings.\n");
        sb.Append("User-agent: *\n");
        foreach (var path in disallow) sb.Append("Disallow: ").Append(path).Append('\n');
        sb.Append("Allow: /\n\n");

        sb.Append("# AI search & assistant crawlers: explicitly welcome\n");
        foreach (var agent in AiAgents) sb.Append("User-agent: ").Append(agent).Append('\n');
        sb.Append("Allow: /\n");
        foreach (var path in disallow) sb.Append("Disallow: ").Append(path).Append('\n');

        sb.Append("\nSitemap: ").Append(sitemapUrl).Append('\n');
        return sb.ToString();
    }
}
