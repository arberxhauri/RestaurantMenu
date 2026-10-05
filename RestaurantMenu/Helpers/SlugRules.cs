using System.Globalization;
using System.Text;

namespace RestaurantMenu.Helpers;

/// <summary>
/// A branch's link segment ("Oliver's Italian" → "olivers-italian"), shared by /menu/{slug},
/// /book/{slug}, /site/{slug}, the free subdomain and printed QR codes. Lowercase ASCII letters,
/// digits and single hyphens, 3 to 60 characters, never a reserved word. Pure rules: the
/// uniqueness check against the database is in BranchSlugs.
/// </summary>
public static class SlugRules
{
    public const int MinLength = 3;
    public const int MaxLength = 60;

    /// <summary>Used when a name has no letters or digits that can be written in ASCII.</summary>
    public const string Fallback = "restaurant";

    /// <summary>
    /// Words a slug can't be: endpoints under /menu, the app's own pages (a subdomain like
    /// admin.myquickmenu.al must never be a restaurant's), and names that look official.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        // Endpoints that share /menu/{x} (MenuController, sw.js).
        "event", "order", "orders", "feedback",
        // App sections and routes.
        "menu", "book", "site", "kitchen", "manage", "admin", "account", "dashboard", "branch", "category",
        "product", "seo", "team", "tables", "bookings", "website", "stock", "shifts", "sales", "insights", "translate",
        "home", "privacy", "terms", "pricing", "signup", "login", "logout", "register", "billing", "checkout",
        "invoice", "invoices", "hubs", "api", "app", "webhooks", "sitemap", "robots", "manifest",
        // Static folders.
        "css", "js", "lib", "img", "images", "assets", "static", "cdn", "media", "files",
        // Hosts and names that would look like the platform's own.
        "www", "mail", "email", "smtp", "imap", "pop", "ftp", "ns1", "ns2", "dns", "mx", "status", "help",
        "support", "docs", "blog", "news", "dev", "test", "staging", "beta", "localhost",
        "myquickmenu", "quickmenu", "mqm", "4cs", "official", "security", "root", "system", "null", "undefined"
    };

    // Letters that don't decompose into a base letter plus accent.
    private static readonly Dictionary<char, string> Special = new()
    {
        ['ß'] = "ss", ['æ'] = "ae", ['œ'] = "oe", ['ø'] = "o", ['đ'] = "d", ['ð'] = "d", ['þ'] = "th",
        ['ł'] = "l", ['ı'] = "i", ['ħ'] = "h", ['ŀ'] = "l", ['ŧ'] = "t"
    };

    /// <summary>
    /// The slug a name asks for, before any uniqueness suffix: accents dropped (ë→e, ç→c, ş→s),
    /// apostrophes removed ("oliver's" → "olivers"), anything else that isn't a letter or digit
    /// becomes one hyphen. Too short → "-menu" added; nothing usable → <see cref="Fallback"/>.
    /// Reserved words come back unchanged: <see cref="IsAvailable"/> turns them away.
    /// </summary>
    public static string FromName(string? name)
    {
        var sb = new StringBuilder();
        var hyphen = false;
        foreach (var c in (name ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\'' or '’' or '‘' or '`' or '´') continue;
            var piece = c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c.ToString() : Special.GetValueOrDefault(c);
            if (piece == null)
            {
                hyphen = sb.Length > 0;
                continue;
            }
            if (hyphen) sb.Append('-');
            hyphen = false;
            sb.Append(piece);
        }

        var slug = Trim(sb.ToString(), MaxLength);
        if (slug.Length == 0) return Fallback;
        return slug.Length < MinLength ? slug + "-menu" : slug;
    }

    /// <summary>
    /// The n-th candidate for a base slug: n = 1 is the base itself, then "base-2", "base-3"…
    /// The base is shortened so the result stays within <see cref="MaxLength"/>.
    /// </summary>
    public static string Candidate(string baseSlug, int n)
    {
        if (n <= 1) return baseSlug;
        var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
        return Trim(baseSlug, MaxLength - suffix.Length) + suffix;
    }

    /// <summary>Well formed and not reserved: what a stored slug must satisfy.</summary>
    public static bool IsValid(string? slug) =>
        slug is { Length: >= MinLength and <= MaxLength }
        && slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && slug[0] != '-' && slug[^1] != '-' && !slug.Contains("--")
        && !Reserved.Contains(slug);

    /// <summary>Valid, and taken neither by another branch's slug nor by another branch's old link.</summary>
    public static bool IsAvailable(string slug, ISet<string> taken) => IsValid(slug) && !taken.Contains(slug);

    /// <summary>
    /// The first free slug for a name: its own slug, else "-2", "-3"… <paramref name="taken"/> holds
    /// every slug and old link already used by other branches.
    /// </summary>
    public static string Unique(string? name, ISet<string> taken)
    {
        var baseSlug = FromName(name);
        for (var n = 1; ; n++)
        {
            var candidate = Candidate(baseSlug, n);
            if (IsAvailable(candidate, taken)) return candidate;
        }
    }

    /// <summary>
    /// The link a branch had before slugs were stored: its name, spaces removed, lowercased
    /// ("Oliver's Italian" → "oliver'sitalian"). Kept as an old link so printed QR codes still open.
    /// </summary>
    public static string Legacy(string name) => name.Replace(" ", string.Empty).ToLowerInvariant();

    /// <summary>A link segment from a request, made comparable with stored slugs and old links.</summary>
    public static string Key(string? routeValue)
    {
        if (string.IsNullOrEmpty(routeValue)) return string.Empty;
        string decoded;
        try { decoded = Uri.UnescapeDataString(routeValue); }
        catch (UriFormatException) { decoded = routeValue; }
        return decoded.Trim().ToLowerInvariant();
    }

    private static string Trim(string slug, int max) =>
        (slug.Length > max ? slug[..max] : slug).Trim('-');
}
