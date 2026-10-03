using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RestaurantMenu.Helpers;

/// <summary>
/// A branch's look on the guest menu, stored as JSON in Branch.ThemeColors:
/// three colours, light/dark preference and header style, plus the colours last taken
/// from the logo (for "Use logo colours"). Reads every older format of that JSON.
///
/// Contrast is guaranteed rather than refused: text on a colour is black or white,
/// whichever contrasts more (one of them always reaches 4.5:1), and coloured text
/// (prices, badges) is darkened or lightened just enough to reach 4.5:1 on the menu's
/// light and dark backgrounds. The same maths runs in site.js for the live preview.
/// </summary>
public class BrandTheme
{
    public const string DefaultPrimary = "#C8642A";
    public const string DefaultSecondary = "#A4501F";
    public const string DefaultAccent = "#7E3D17";

    public static readonly string[] Appearances = { "auto", "light", "dark" };
    public static readonly string[] Headers = { "photo", "colour", "minimal" };

    // The menu's backgrounds (menu.css): the darker light one and the lighter dark one,
    // i.e. the worst case for each mode.
    public const string LightBackground = "#F4F4F2";
    public const string DarkBackground = "#1C1C1F";
    public const string DarkText = "#141414";

    public string Primary { get; set; } = DefaultPrimary;
    public string Secondary { get; set; } = DefaultSecondary;
    public string Accent { get; set; } = DefaultAccent;
    /// <summary>auto (follow the guest's phone), light or dark.</summary>
    public string Appearance { get; set; } = "auto";
    /// <summary>photo (banner, falls back to colour without one), colour or minimal.</summary>
    public string Header { get; set; } = "photo";
    public string? LogoPrimary { get; set; }
    public string? LogoSecondary { get; set; }
    public string? LogoAccent { get; set; }

    private static readonly Regex HexPattern = new("^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);

    /// <summary>"#c8642a", "c8642a" or "#c62" → "#C8642A"; null if it isn't a colour.</summary>
    public static string? NormalizeHex(string? value)
    {
        if (value == null) return null;
        var m = HexPattern.Match(value.Trim());
        if (!m.Success) return null;
        var h = m.Groups[1].Value;
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        return "#" + h.ToUpperInvariant();
    }

    public static BrandTheme Parse(string? json)
    {
        var theme = new BrandTheme();
        if (string.IsNullOrWhiteSpace(json)) return theme;
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? Get(string name) =>
                doc.RootElement.EnumerateObject()
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    .Value is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

            theme.Primary = NormalizeHex(Get("Primary")) ?? theme.Primary;
            theme.Secondary = NormalizeHex(Get("Secondary")) ?? theme.Secondary;
            theme.Accent = NormalizeHex(Get("Accent")) ?? theme.Accent;
            theme.Appearance = Appearances.Contains(Get("Appearance")) ? Get("Appearance")! : "auto";
            theme.Header = Headers.Contains(Get("Header")) ? Get("Header")! : "photo";
            theme.LogoPrimary = NormalizeHex(Get("LogoPrimary"));
            theme.LogoSecondary = NormalizeHex(Get("LogoSecondary"));
            theme.LogoAccent = NormalizeHex(Get("LogoAccent"));
            // Before the editor, the stored colours were the logo's.
            if (theme.LogoPrimary == null && Get("Primary") != null)
            {
                (theme.LogoPrimary, theme.LogoSecondary, theme.LogoAccent) = (theme.Primary, theme.Secondary, theme.Accent);
            }
        }
        catch (JsonException)
        {
            // keep the defaults
        }
        return theme;
    }

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, string?>
    {
        ["Primary"] = Primary, ["Secondary"] = Secondary, ["Accent"] = Accent,
        ["Appearance"] = Appearance, ["Header"] = Header,
        ["LogoPrimary"] = LogoPrimary, ["LogoSecondary"] = LogoSecondary, ["LogoAccent"] = LogoAccent
    });

    // ---------------------------------------------------------------- contrast (WCAG 2)

    private static (double R, double G, double B) Rgb(string hex)
    {
        var h = NormalizeHex(hex) ?? DefaultPrimary;
        return (int.Parse(h.Substring(1, 2), NumberStyles.HexNumber),
                int.Parse(h.Substring(3, 2), NumberStyles.HexNumber),
                int.Parse(h.Substring(5, 2), NumberStyles.HexNumber));
    }

    public static double Luminance(string hex)
    {
        static double C(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        var (r, g, b) = Rgb(hex);
        return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Near-black or white text on <paramref name="background"/>, whichever reads better.
    /// If neither reaches 4.5:1, pure black does (max(white, black) is always >= 4.58:1).
    /// </summary>
    public static string TextOn(string background)
    {
        double w = Contrast("#FFFFFF", background), d = Contrast(DarkText, background);
        if (Math.Max(w, d) >= 4.5) return w >= d ? "#FFFFFF" : DarkText;
        return "#000000";
    }

    /// <summary>Text for a gradient between two colours: the one whose worse contrast is better.</summary>
    public static string TextOn(string a, string b)
    {
        double w = Math.Min(Contrast("#FFFFFF", a), Contrast("#FFFFFF", b));
        double d = Math.Min(Contrast(DarkText, a), Contrast(DarkText, b));
        if (Math.Max(w, d) >= 4.5 || w >= d) return w >= d ? "#FFFFFF" : DarkText;
        return "#000000";
    }

    /// <summary>
    /// The colour itself if it reads at <paramref name="min"/>:1 on <paramref name="background"/>,
    /// otherwise the nearest shade towards black (light background) or white (dark) that does.
    /// </summary>
    public static string Readable(string color, string background, double min = 4.5)
    {
        var c = NormalizeHex(color) ?? DefaultPrimary;
        if (Contrast(c, background) >= min) return c;
        var target = Luminance(background) > 0.5 ? (0.0, 0.0, 0.0) : (255.0, 255.0, 255.0);
        var (r, g, b) = Rgb(c);
        for (var t = 0.04; t <= 1.0001; t += 0.04)
        {
            var mixed = Hex(r + (target.Item1 - r) * t, g + (target.Item2 - g) * t, b + (target.Item3 - b) * t);
            if (Contrast(mixed, background) >= min) return mixed;
        }
        return Luminance(background) > 0.5 ? DarkText : "#FFFFFF";
    }

    private static string Hex(double r, double g, double b) =>
        $"#{(int)Math.Round(r):X2}{(int)Math.Round(g):X2}{(int)Math.Round(b):X2}";

    // What the menu uses (all derived; nothing here needs saving).
    public string OnPrimary => TextOn(Primary);
    public string OnAccent => TextOn(Accent);
    public string HeaderText => TextOn(Primary, Secondary);
    public string PrimaryInkLight => Readable(Primary, LightBackground);
    public string PrimaryInkDark => Readable(Primary, DarkBackground);
    public string AccentInkLight => Readable(Accent, LightBackground);
    public string AccentInkDark => Readable(Accent, DarkBackground);

    /// <summary>
    /// Plain-language notes when a colour is clearly unreadable as chosen (under 3:1) on a
    /// background the menu actually uses. Small adjustments, like the lighter shade nearly
    /// every colour needs on a dark menu, happen silently.
    /// </summary>
    public List<string> ContrastNotes()
    {
        var notes = new List<string>();
        bool light = Appearance != "dark", dark = Appearance != "light";
        if (light && Contrast(Primary, LightBackground) < 3) notes.Add("The main colour is very light, so prices on a light menu use a darker shade of it.");
        if (dark && Contrast(Primary, DarkBackground) < 3) notes.Add("The main colour is very dark, so prices on a dark menu use a lighter shade of it.");
        if ((light && Contrast(Accent, LightBackground) < 3) || (dark && Contrast(Accent, DarkBackground) < 3))
            notes.Add("Badge text uses an adjusted shade of the highlight colour so it stays readable.");
        return notes;
    }
}
