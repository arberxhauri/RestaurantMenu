using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;
using System.Text.RegularExpressions;

namespace RestaurantMenu.Helpers.Landing;

/// <summary>
/// The marketing site's content pages (product pages, FAQ, about, contact) as data, in English
/// and Albanian (LandingContent.En.cs / LandingContent.Sq.cs). One view renders them
/// (Views/Landing/Page.cshtml); the same data feeds the FAQPage schema, llms-full.txt and the
/// tests, so what search engines and AI assistants read is exactly what visitors see.
///
/// Writing rules (docs/SEO-GEO.md, "Writing for AI answers"): open with a definition that can be
/// quoted on its own, keep the brand definition word for word the same, use question-style
/// headings, concrete facts only (prices come from the price book through tokens), short
/// self-contained paragraphs. Never invent numbers: anything unknown is a [TODO: …].
///
/// Tokens in any text: {definition} the brand definition, {menu} {guests} {everything} the
/// monthly price of each plan, {trial} the free trial days, {website} {bookings} {ordering}
/// {management} {domain} the monthly price per branch of each module. Links: [text](/path).
/// </summary>
public static partial class LandingContent
{
    public record Page(
        string Key,
        string Path,
        string MetaTitle,
        string MetaDescription,
        string H1,
        string Lede,
        string Photo,
        string PhotoAlt,
        IReadOnlyList<Section> Sections,
        string? FeaturesTitle = null,
        IReadOnlyList<Feature>? Features = null,
        string? StepsTitle = null,
        IReadOnlyList<Step>? Steps = null,
        Table? Comparison = null,
        IReadOnlyList<Screenshot>? Screenshots = null,
        string? FaqTitle = null,
        IReadOnlyList<Faq>? Faqs = null,
        bool PricingTeaser = true,
        string? CtaTitle = null,
        string? CtaText = null,
        string? Breadcrumb = null);

    /// <param name="Id">Anchor for in-page links, e.g. "how-much".</param>
    public record Section(string Heading, IReadOnlyList<string> Paragraphs, IReadOnlyList<string>? Bullets = null, string? Id = null);
    public record Feature(string Icon, string Title, string Text);
    public record Step(string Title, string Text);
    public record Faq(string Question, string Answer);
    public record Table(string Heading, string Caption, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
    /// <param name="Image">Under wwwroot, e.g. /img/screens/menu-en.webp.</param>
    public record Screenshot(string Image, int Width, int Height, string Alt, string Caption);

    /// <summary>When the pages' facts were last checked; shown as "Last updated" on each page.</summary>
    public static readonly DateOnly LastUpdated = new(2026, 10, 11);

    /// <summary>Product pages in nav order, then the rest.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "digital-menu", "restaurant-website", "table-bookings", "restaurant-management", "faq", "about", "contact"
    };

    public static Page Get(string key, string language) =>
        (language == "sq" ? Sq : En).TryGetValue(key, out var page) ? page : throw new KeyNotFoundException(key);

    public static IReadOnlyDictionary<string, Page> All(string language) => language == "sq" ? Sq : En;

    /// <summary>The FAQ page's questions: its own, then every product page's, without duplicates.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<Faq> Faqs)> FaqGroups(string language)
    {
        var all = All(language);
        var groups = new List<(string, IReadOnlyList<Faq>)> { (all["faq"].FaqTitle ?? all["faq"].H1, all["faq"].Faqs ?? Array.Empty<Faq>()) };
        var seen = new HashSet<string>(groups[0].Item2.Select(f => f.Question));
        foreach (var key in new[] { "digital-menu", "restaurant-website", "table-bookings", "restaurant-management" })
        {
            var p = all[key];
            var faqs = (p.Faqs ?? Array.Empty<Faq>()).Where(f => seen.Add(f.Question)).ToList();
            if (faqs.Count > 0) groups.Add((p.Breadcrumb ?? p.H1, faqs));
        }
        return groups;
    }

    // ------------------------------------------------------------------ rendering text

    /// <summary>
    /// Whether [TODO: …] notes are shown. On in Development (Program.cs) so they are seen and filled
    /// in; elsewhere they are left out, so an unfinished fact never reaches visitors or crawlers.
    /// </summary>
    public static bool ShowTodos { get; set; } = true;

    /// <summary>Fills the tokens. Unknown prices read "price on request" rather than a made-up number.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> tokens)
    {
        var filled = Token().Replace(text, m => tokens.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return ShowTodos ? filled : Todo().Replace(filled, "").Trim();
    }

    [GeneratedRegex(@"\s*\[TODO:[^\]]*\]")]
    private static partial Regex Todo();

    /// <summary>The [TODO: …] notes in a text (for the handover list and tests).</summary>
    public static IEnumerable<string> Todos(string text) => Todo().Matches(text).Select(m => m.Value.Trim());

    [GeneratedRegex(@"\{([a-z]+)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    /// <summary>Like the site's Razor encoder (Program.cs): letters of every language stay as they are.</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Text with [label](/path) links as HTML: everything encoded, links only to this site's own
    /// paths, https:// or mailto:/tel: (anything else stays plain text).
    /// </summary>
    public static string Html(string text, string language = "en")
    {
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in Link().Matches(text))
        {
            sb.Append(Encoder.Encode(text[last..m.Index]));
            var href = m.Groups[2].Value;
            var ok = href.StartsWith('/') && !href.StartsWith("//") || href.StartsWith("https://") || href.StartsWith("mailto:") || href.StartsWith("tel:");
            if (ok && href.StartsWith('/')) href = Localize(href, language);
            sb.Append(ok
                ? $"<a href=\"{Encoder.Encode(href)}\">{Encoder.Encode(m.Groups[1].Value)}</a>"
                : Encoder.Encode(m.Groups[1].Value));
            last = m.Index + m.Length;
        }
        sb.Append(Encoder.Encode(text[last..]));
        return sb.ToString();
    }

    /// <summary>Text with links as "label (https://…/path)", for llms-full.txt.</summary>
    public static string Plain(string text, string baseUrl) =>
        Link().Replace(text, m => m.Groups[2].Value.StartsWith('/') ? $"{m.Groups[1].Value} ({baseUrl}{m.Groups[2].Value})" : $"{m.Groups[1].Value} ({m.Groups[2].Value})");

    /// <summary>A link to a marketing page in a language: "/faq" or "/faq?lang=sq" (keeps any #anchor).</summary>
    public static string Localize(string path, string language)
    {
        if (language == "en" || !path.StartsWith('/')) return path;
        var hash = path.IndexOf('#');
        var (p, anchor) = hash < 0 ? (path, "") : (path[..hash], path[hash..]);
        return p + (p.Contains('?') ? "&" : "?") + "lang=" + language + anchor;
    }

    /// <summary>"11 October 2026" / "11 tetor 2026".</summary>
    public static string Date(DateOnly d, string language) => language == "sq"
        ? $"{d.Day} {SqMonths[d.Month - 1]} {d.Year}"
        : d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static readonly string[] SqMonths =
        { "janar", "shkurt", "mars", "prill", "maj", "qershor", "korrik", "gusht", "shtator", "tetor", "nëntor", "dhjetor" };
}
