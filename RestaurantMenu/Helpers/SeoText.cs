using System.Text.RegularExpressions;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Titles and descriptions for restaurant pages, written from the restaurant's real data in the
/// page's language (never English meta on an Albanian page). The owner's own title and
/// description (Search &amp; sharing) win when set. Pure functions, tested in SeoTextTests.
/// </summary>
public static partial class SeoText
{
    public const int TitleMax = 60;
    public const int DescriptionMax = 160;

    // ------------------------------------------------------------------ text hygiene

    /// <summary>
    /// Names that are clearly not real menu content ("Test", "Testing 2", "asdf", "Lorem ipsum"):
    /// kept out of descriptions and structured data. The guest menu still shows whatever the owner entered.
    /// </summary>
    public static bool IsPlaceholder(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return PlaceholderWord().IsMatch(name) || PlaceholderWhole().IsMatch(name.Trim());
    }

    [GeneratedRegex(@"\b(test|testing|tests|dummy|lorem|ipsum|asdf\w*|qwerty|placeholder)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderWord();

    [GeneratedRegex(@"^(demo|sample|example|prova|provë|tmp|temp|x+|abc|aaa+|new category|category \d+|kategori\s*\d*|[\W\d_]+)(\s*\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderWhole();

    /// <summary>One line, single spaces.</summary>
    public static string OneLine(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Shortened to <paramref name="max"/> characters at a word boundary, never mid-word, with "…"
    /// only when something was cut, and no dangling comma or colon before it.
    /// </summary>
    public static string Trim(string? text, int max)
    {
        var s = OneLine(text);
        if (s.Length <= max) return s;
        var cut = s[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > max / 2) cut = cut[..space];
        return cut.TrimEnd(' ', ',', ';', ':', '-', '–', '—', '·', '.') + "…";
    }

    /// <summary>The first parts that fit within <paramref name="max"/>, joined with " · ".</summary>
    public static string JoinFitting(int max, params string?[] parts)
    {
        var result = "";
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var next = result.Length == 0 ? p.Trim() : result + " · " + p.Trim();
            if (next.Length > max) break;
            result = next;
        }
        return result.Length == 0 ? Trim(parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)), max) : result;
    }

    // ------------------------------------------------------------------ words per language

    private record Words(
        string MenuAndPrices,      // "{0} Menu & Prices"
        string MenuIntro,          // "See the full menu of {0}"
        string MenuIntroCity,      // "See the full menu of {0} in {1}"
        string Prices, string Photos, string Allergens, string Diets, string And,
        string UpdatedBy,          // "{0}, updated by the restaurant."
        string Languages,          // "Menu in {0} languages."
        string SiteFallback,       // "{0}: menu, opening hours and location." ({0} = name and what it is)
        string SiteFallbackBook,   // "... and online table booking."
        string BookTitle,          // "Book a table · {0}"
        string BookDescription);   // "Book a table at {0} online..."

    private static readonly Dictionary<string, Words> Table = new()
    {
        ["en"] = new("{0} Menu & Prices", "See the full menu of {0}", "See the full menu of {0} in {1}",
            "prices", "photos", "allergens", "dietary options", "and", "{0}, updated by the restaurant.", "Menu in {0} languages.",
            "{0}: menu, opening hours and location.", "{0}: menu, opening hours, location and online table booking.",
            "Book a table · {0}", "Book a table at {0} online: pick a day and time and get your confirmation straight away."),
        ["sq"] = new("{0}: menuja dhe çmimet", "Menuja e plotë e {0}", "Menuja e plotë e {0}, {1}",
            "çmimet", "fotot", "alergjenët", "opsionet dietike", "dhe", "{0}, të përditësuara nga restoranti.", "Menuja në {0} gjuhë.",
            "{0}: menuja, orari dhe vendndodhja.", "{0}: menuja, orari, vendndodhja dhe rezervimi online i tavolinës.",
            "Rezervo një tavolinë · {0}", "Rezervo online një tavolinë te {0}: zgjidh ditën dhe orën, dhe merr konfirmimin menjëherë."),
        ["it"] = new("{0}: menu e prezzi", "Il menu completo di {0}", "Il menu completo di {0} a {1}",
            "prezzi", "foto", "allergeni", "opzioni dietetiche", "e", "{0}, aggiornati dal ristorante.", "Menu in {0} lingue.",
            "{0}: menu, orari e posizione.", "{0}: menu, orari, posizione e prenotazione del tavolo online.",
            "Prenota un tavolo · {0}", "Prenota online un tavolo da {0}: scegli giorno e ora e ricevi subito la conferma."),
        ["de"] = new("{0}: Speisekarte & Preise", "Die komplette Speisekarte von {0}", "Die komplette Speisekarte von {0} in {1}",
            "Preise", "Fotos", "Allergene", "Ernährungshinweise", "und", "{0}, vom Restaurant aktuell gehalten.", "Speisekarte in {0} Sprachen.",
            "{0}: Speisekarte, Öffnungszeiten und Anfahrt.", "{0}: Speisekarte, Öffnungszeiten, Anfahrt und Online-Tischreservierung.",
            "Tisch reservieren · {0}", "Reservieren Sie online einen Tisch bei {0}: Tag und Uhrzeit wählen, Bestätigung sofort erhalten."),
        ["fr"] = new("{0} : menu et prix", "Le menu complet de {0}", "Le menu complet de {0} à {1}",
            "prix", "photos", "allergènes", "options alimentaires", "et", "{0}, mis à jour par le restaurant.", "Menu en {0} langues.",
            "{0} : menu, horaires et adresse.", "{0} : menu, horaires, adresse et réservation de table en ligne.",
            "Réserver une table · {0}", "Réservez une table en ligne chez {0} : choisissez le jour et l'heure, confirmation immédiate."),
        ["es"] = new("{0}: carta y precios", "La carta completa de {0}", "La carta completa de {0} en {1}",
            "precios", "fotos", "alérgenos", "opciones dietéticas", "y", "{0}, actualizados por el restaurante.", "Carta en {0} idiomas.",
            "{0}: carta, horario y ubicación.", "{0}: carta, horario, ubicación y reserva de mesa online.",
            "Reservar mesa · {0}", "Reserva mesa online en {0}: elige día y hora y recibe la confirmación al instante."),
        ["tr"] = new("{0} menü ve fiyatlar", "{0} menüsünün tamamı", "{0} ({1}) menüsünün tamamı",
            "fiyatlar", "fotoğraflar", "alerjenler", "diyet seçenekleri", "ve", "{0}, restoran tarafından güncellenir.", "Menü {0} dilde.",
            "{0}: menü, çalışma saatleri ve konum.", "{0}: menü, çalışma saatleri, konum ve online masa rezervasyonu.",
            "Masa rezervasyonu · {0}", "{0} için online masa ayırtın: günü ve saati seçin, onayı hemen alın.")
    };

    private static Words W(string language) => Table.TryGetValue(language, out var w) ? w : Table["en"];

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------------ menu

    /// <summary>What a menu page's description draws on: the real, visible facts.</summary>
    public record MenuFacts(string Name, string? City, IReadOnlyList<string> Categories, bool HasPhotos, bool HasAllergens, bool HasDiets, int LanguageCount = 1);

    private static readonly Dictionary<string, string> MenuNames = new()
    {
        ["en"] = "{0} menu", ["sq"] = "Menuja e {0}", ["it"] = "Menu di {0}", ["de"] = "Speisekarte von {0}",
        ["fr"] = "Menu de {0}", ["es"] = "Carta de {0}", ["tr"] = "{0} menüsü"
    };

    /// <summary>The menu's own name: "Oliver's Italian menu", "Menuja e Oliva".</summary>
    public static string MenuName(string name, string language) =>
        string.Format(MenuNames.TryGetValue(language, out var f) ? f : MenuNames["en"], name);

    /// <summary>"{Name} Menu & Prices · {City}", shortened to fit 60 characters (city first, then the suffix).</summary>
    public static string MenuTitle(string name, string? city, string language)
    {
        var withSuffix = string.Format(W(language).MenuAndPrices, name);
        if (!string.IsNullOrWhiteSpace(city) && (withSuffix + " · " + city).Length <= TitleMax) return withSuffix + " · " + city;
        if (withSuffix.Length <= TitleMax) return withSuffix;
        return Trim(name, TitleMax);
    }

    /// <summary>
    /// "See the full menu of {Name} in {City}: {up to 3 real categories}. Prices, photos, allergens and
    /// dietary options, updated by the restaurant." Only the features the menu really has are named,
    /// placeholder categories are skipped, and the category list shrinks until it fits 160 characters.
    /// </summary>
    public static string MenuDescription(MenuFacts f, string language)
    {
        var w = W(language);
        var intro = string.IsNullOrWhiteSpace(f.City) ? string.Format(w.MenuIntro, f.Name) : string.Format(w.MenuIntroCity, f.Name, f.City);
        var features = new List<string> { w.Prices };
        if (f.HasPhotos) features.Add(w.Photos);
        if (f.HasAllergens) features.Add(w.Allergens);
        if (f.HasDiets) features.Add(w.Diets);
        var list = features.Count == 1 ? features[0] : string.Join(", ", features.Take(features.Count - 1)) + " " + w.And + " " + features[^1];
        var tail = string.Format(w.UpdatedBy, Capitalize(list));
        // A real, useful fact when there's room: how many languages the menu is in.
        if (f.LanguageCount > 1) tail += " " + string.Format(w.Languages, f.LanguageCount);

        var categories = f.Categories.Where(c => !IsPlaceholder(c)).Select(OneLine).Distinct().Take(3).ToList();
        for (var n = categories.Count; n >= 0; n--)
        {
            var head = n == 0 ? intro + "." : $"{intro}{(language == "fr" ? " :" : ":")} {string.Join(", ", categories.Take(n))}.";
            var text = head + " " + tail;
            if (text.Length <= DescriptionMax) return text;
            var shorter = head + " " + string.Format(w.UpdatedBy, Capitalize(list));
            if (shorter.Length <= DescriptionMax) return shorter;
            if (n == 0) return head.Length <= DescriptionMax ? head : Trim(head, DescriptionMax);
        }
        return Trim(intro, DescriptionMax);
    }

    // ------------------------------------------------------------------ website

    /// <summary>"{Name} · Italian restaurant · Tirana", parts dropped from the end until it fits.</summary>
    public static string SiteTitle(string name, BusinessType type, string? cuisine, string? city, string language) =>
        JoinFitting(TitleMax, name, Cuisines.Phrase(type, cuisine, language), city);

    /// <summary>
    /// The website's description: the restaurant's own about text (or tagline) cleanly shortened,
    /// else "{Name}, Italian restaurant in Tirana: menu, opening hours, location and online table booking."
    /// </summary>
    public static string SiteDescription(string name, string? about, string? tagline, BusinessType type, string? cuisine, string? city, bool bookable, string language)
    {
        if (!string.IsNullOrWhiteSpace(about)) return Trim(about, DescriptionMax - 5);
        if (!string.IsNullOrWhiteSpace(tagline) && OneLine(tagline).Length >= 50) return Trim(tagline, DescriptionMax - 5);
        // "Restaurant" on its own adds nothing to a description; a cuisine or another kind of place does.
        var what = type == BusinessType.Restaurant && Cuisines.Find(cuisine) == null ? null : Cuisines.Phrase(type, cuisine, language);
        var who = string.Join(", ", new[] { name, what, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var w = W(language);
        return Trim(string.Format(bookable ? w.SiteFallbackBook : w.SiteFallback, who), DescriptionMax);
    }

    // ------------------------------------------------------------------ booking

    public static string BookTitle(string name, string language) => Trim(string.Format(W(language).BookTitle, name), TitleMax);
    public static string BookDescription(string name, string language) => Trim(string.Format(W(language).BookDescription, name), DescriptionMax);

    // ------------------------------------------------------------------ owner's own words

    /// <summary>
    /// The owner's title or description for this language (Search &amp; sharing), or null. The base
    /// text is in <paramref name="baseLanguage"/> (SeoLanguages.BaseTextLanguage); another language
    /// only gets its own translation, never the base text, so no page gets meta in the wrong language.
    /// </summary>
    public static string? Own(string? baseText, string? translations, string language, string baseLanguage)
    {
        if (language == baseLanguage) return string.IsNullOrWhiteSpace(baseText) ? null : OneLine(baseText);
        if (string.IsNullOrEmpty(translations)) return null;
        try
        {
            var all = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(translations);
            return all != null && all.TryGetValue(language, out var tr) && !string.IsNullOrWhiteSpace(tr) ? OneLine(tr) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
