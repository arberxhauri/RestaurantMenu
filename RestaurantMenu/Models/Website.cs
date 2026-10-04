namespace RestaurantMenu.Models;

/// <summary>
/// A branch's website (Website page): what only the site needs. Everything else (name,
/// photos, colours, hours, dishes, bookings) comes from the dashboard as it is, so the site
/// is never out of date. One row per branch, created when the website is first saved.
/// </summary>
public class BranchSite
{
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>Published: visible to everyone. Off: only the team sees a preview.</summary>
    public bool Enabled { get; set; }
    /// <summary>One line under the name, e.g. "Seafood by the harbour since 1998".</summary>
    public string? Tagline { get; set; }
    public string? TaglineTranslations { get; set; }
    public string? About { get; set; }
    public string? AboutTranslations { get; set; }
    /// <summary>Instagram username (without @) and Facebook page address.</summary>
    public string? Instagram { get; set; }
    public string? Facebook { get; set; }
    public bool ShowHighlights { get; set; } = true;
    public bool ShowMap { get; set; } = true;
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// The restaurant's own domain (www.example.al) pointing at this app. Requests for a
/// verified host are routed to the branch's website by SiteHostMiddleware. Verified means
/// the app reached itself through that host (DNS really points here); the TLS certificate is
/// Render's, issued once the domain is added there.
/// </summary>
public class Domain
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>Lower case, no scheme, port or trailing dot; internationalised names in punycode.</summary>
    public string Host { get; set; } = "";
    public bool Verified { get; set; }
    /// <summary>Answered at http://{Host}/.well-known/mqm-domain, so the check knows it reached this app.</summary>
    public string Token { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
    public DateTime? VerifiedUtc { get; set; }
    public DateTime? LastCheckUtc { get; set; }
    /// <summary>Why the last check failed, in words for the owner.</summary>
    public string? LastError { get; set; }
    /// <summary>Render's id for the domain when added through the Render API.</summary>
    public string? RenderId { get; set; }
}
