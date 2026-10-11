namespace RestaurantMenu.Helpers;

/// <summary>
/// The marketing site's pages: one list for the sitemap, llms.txt, the footer and the tests, so a
/// new page is added in one place. Every page is English on the bare URL and Albanian on ?lang=sq
/// unless <see cref="MarketingPage.Languages"/> says otherwise.
/// </summary>
public static class MarketingPages
{
    public record MarketingPage(string Key, string Path, string ChangeFrequency, string Priority, IReadOnlyList<string> Languages);

    public static readonly IReadOnlyList<string> Bilingual = new[] { "en", "sq" };
    private static readonly IReadOnlyList<string> EnglishOnly = new[] { "en" };

    public static readonly IReadOnlyList<MarketingPage> All = new[]
    {
        new MarketingPage("home", "/", "weekly", "1.0", Bilingual),
        new MarketingPage("pricing", "/pricing", "weekly", "0.9", Bilingual),
        new MarketingPage("privacy", "/home/privacy", "yearly", "0.2", EnglishOnly),
        new MarketingPage("terms", "/home/terms", "yearly", "0.2", EnglishOnly)
    };

    public static MarketingPage Get(string key) => All.First(p => p.Key == key);

    /// <summary>The page in a language: the bare path for English, ?lang=xx otherwise.</summary>
    public static string PathFor(MarketingPage page, string language) =>
        page.Path + SeoLanguages.Query(language, "en");

    /// <summary>The marketing site's language for ?lang=: sq or en, nothing else.</summary>
    public static (string Language, bool Redirect) Resolve(MarketingPage page, string? requested) =>
        SeoLanguages.Resolve(page.Languages, "en", requested);
}
