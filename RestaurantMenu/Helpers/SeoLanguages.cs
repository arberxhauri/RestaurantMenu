using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// One rule for every public page that comes in several languages (menu, website, booking,
/// marketing pages): the main language lives on the bare URL, every other language on ?lang=xx,
/// and anything else is sent to the bare URL with a 301, so each language has exactly one address.
/// </summary>
public static class SeoLanguages
{
    /// <summary>The languages a branch's guests can pick, in the order the owner saved them.</summary>
    public static string[] Of(Branch b)
    {
        var list = (b.SupportedLanguages ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        return list.Length == 0 ? new[] { "en" } : list;
    }

    /// <summary>The branch's main language: the owner's choice if still offered, else English if offered, else the first.</summary>
    public static string Default(Branch b) => Default(Of(b), b.DefaultLanguage);

    public static string Default(IReadOnlyList<string> languages, string? chosen) =>
        chosen != null && languages.Contains(chosen) ? chosen
        : languages.Contains("en") ? "en"
        : languages.Count > 0 ? languages[0] : "en";

    /// <summary>
    /// The language of a branch's base text (dish names, about text, its own SEO title): the app
    /// stores English there whenever the menu has English and asks for the others as translations
    /// (TranslationHelper), so English if offered, else the main language.
    /// </summary>
    public static string BaseTextLanguage(Branch b)
    {
        var languages = Of(b);
        return languages.Contains("en") ? "en" : Default(languages, b.DefaultLanguage);
    }

    /// <summary>
    /// The language to render for ?lang=<paramref name="requested"/>, and whether the request must
    /// instead be redirected (301) to the bare URL: ?lang= the main language (a duplicate of the bare
    /// URL) or a language the page doesn't have (no soft duplicates).
    /// </summary>
    public static (string Language, bool Redirect) Resolve(IReadOnlyList<string> languages, string defaultLanguage, string? requested)
    {
        if (requested == null) return (defaultLanguage, false);
        if (requested == defaultLanguage || !languages.Contains(requested)) return (defaultLanguage, true);
        return (requested, false);
    }

    /// <summary>"?lang=xx" for a non-main language, "" for the main one.</summary>
    public static string Query(string language, string defaultLanguage) =>
        language == defaultLanguage ? "" : "?lang=" + Uri.EscapeDataString(language);

    /// <summary>
    /// The current query string without lang (and without tracking parameters), for the 301 to the
    /// bare URL: a table's ?t=&amp;k= from a printed QR code survives the redirect.
    /// </summary>
    public static string QueryWithout(IQueryCollection query, params string[] also)
    {
        var kept = query.Where(q => q.Key != "lang" && !also.Contains(q.Key) && !IsTracking(q.Key))
            .SelectMany(q => q.Value.Select(v => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(v ?? "")))
            .ToList();
        return kept.Count == 0 ? "" : "?" + string.Join("&", kept);
    }

    /// <summary>Parameters that never belong in a canonical URL.</summary>
    public static bool IsTracking(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || key is "fbclid" or "gclid" or "msclkid" or "ref" or "mc_cid" or "mc_eid" or "igshid";

    /// <summary>Open Graph locale (language_TERRITORY) for a page language.</summary>
    public static string OgLocale(string language) => language switch
    {
        "sq" => "sq_AL",
        "en" => "en_US",
        "it" => "it_IT",
        "de" => "de_DE",
        "fr" => "fr_FR",
        "es" => "es_ES",
        "tr" => "tr_TR",
        _ => language
    };
}
