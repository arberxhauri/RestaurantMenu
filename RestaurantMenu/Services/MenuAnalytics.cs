using System.Text.RegularExpressions;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Records anonymous menu events. Decides what counts (real guests only) and never lets
/// a failure here break the menu: analytics is best-effort, the menu is not.
/// </summary>
public class MenuAnalytics
{
    // Crawlers, link previews (a menu link pasted into WhatsApp is fetched by WhatsApp),
    // headless browsers, uptime monitors and scripts. Matched against the User-Agent,
    // which is only inspected here and never stored.
    private static readonly Regex NotAGuest = new(
        @"bot|crawl|spider|slurp|preview|facebookexternalhit|whatsapp|telegram|discord|skype|embedly|vkshare|pinterest|" +
        @"lighthouse|headless|phantom|curl|wget|python|httpclient|okhttp|java/|go-http|axios|node-fetch|monitor|uptime|pingdom",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MenuAnalytics> _logger;

    public MenuAnalytics(IServiceScopeFactory scopes, ILogger<MenuAnalytics> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>
    /// True for a guest's browser. False for bots and link previews, browser prefetches,
    /// and anyone signed in (owners checking their own menu would inflate the numbers).
    /// </summary>
    public static bool ShouldCount(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true) return false;

        var request = context.Request;
        var userAgent = request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent) || NotAGuest.IsMatch(userAgent)) return false;

        // Speculative loads (link prefetch / prerender) aren't a guest reading the menu.
        if (request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || request.Headers["Purpose"].ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task RecordAsync(int branchId, MenuEventType type, int? productId = null, string? language = null)
    {
        try
        {
            // Own scope and context: the caller's context stays untouched if this fails.
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.MenuEvents.Add(new MenuEvent
            {
                BranchId = branchId,
                ProductId = productId,
                Type = type,
                Lang = language is { Length: > 0 and <= 8 } ? language : null,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record menu event {Type} for branch {BranchId}", type, branchId);
        }
    }
}
