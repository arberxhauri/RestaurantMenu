namespace RestaurantMenu.Models;

/// <summary>
/// A visit to a public page that came from an AI assistant (ChatGPT, Perplexity, Claude, Gemini,
/// Copilot), by its referrer or utm_source. Anonymous on purpose: which assistant, which host and
/// path, when. No IP address, browser or cookie. Admin → AI referrals reports them; kept as long as
/// menu analytics (Analytics:RetentionDays). See Helpers/AiReferrals.
/// </summary>
public class ReferralVisit
{
    public long Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    /// <summary>"ChatGPT", "Perplexity", "Claude", "Gemini", "Copilot".</summary>
    public string Source { get; set; } = "";
    /// <summary>How it was recognised: the referrer's host, or "utm_source=…".</summary>
    public string Via { get; set; } = "";
    /// <summary>The host the visit landed on (myquickmenu.com or a restaurant's own domain).</summary>
    public string Host { get; set; } = "";
    /// <summary>The page, without its query string.</summary>
    public string Path { get; set; } = "";
}
