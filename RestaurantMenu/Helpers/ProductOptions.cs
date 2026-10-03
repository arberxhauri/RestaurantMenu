using System.Globalization;
using System.Text.Json;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Sizes and add-ons: the dish form's repeater (parse, validate, show back), the data the
/// guest menu's dish sheet needs, and the guest-facing words.
/// </summary>
public static class ProductOptions
{
    public const int MaxGroups = 10;
    public const int MaxOptionsPerGroup = 30;
    public const int MaxNameLength = 60;
    public const decimal MaxDelta = 100000m;

    // ---------------------------------------------------------------- form model

    public class OptionForm
    {
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
        public Dictionary<string, string> Translations { get; set; } = new();
    }

    public class GroupForm
    {
        public string Name { get; set; } = "";
        public bool Required { get; set; }
        public string Max { get; set; } = "1";
        public Dictionary<string, string> Translations { get; set; } = new();
        public List<OptionForm> Options { get; set; } = new();
    }

    /// <summary>Form field names, e.g. og-0-name, og-0-o-2-price, og-0-o-2-tr-sq. site.js renumbers them on submit.</summary>
    public static string Field(int g, string part) => $"og-{g}-{part}";
    public static string OptionField(int g, int o, string part) => $"og-{g}-o-{o}-{part}";

    private static string Delta(decimal d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    public static List<GroupForm> ToForm(IEnumerable<ProductOptionGroup>? groups, IEnumerable<string> languages) =>
        (groups ?? Enumerable.Empty<ProductOptionGroup>())
            .OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id)
            .Select(g => new GroupForm
            {
                Name = g.Name,
                Required = g.MinSelect > 0,
                Max = g.MaxSelect.ToString(CultureInfo.InvariantCulture),
                Translations = Translations(g.NameTranslations, languages),
                Options = g.Options.OrderBy(o => o.DisplayOrder).ThenBy(o => o.Id).Select(o => new OptionForm
                {
                    Name = o.Name,
                    Price = o.PriceDelta == 0 ? "" : Delta(o.PriceDelta),
                    Translations = Translations(o.NameTranslations, languages)
                }).ToList()
            }).ToList();

    private static Dictionary<string, string> Translations(string? json, IEnumerable<string> languages) =>
        languages.ToDictionary(l => l, l => TranslationHelper.GetTranslation("", json, l));

    /// <summary>Reads the repeater. Stops at the first missing index, so rows must be numbered 0..n.</summary>
    public static List<GroupForm> FromForm(IFormCollection form, IEnumerable<string> languages)
    {
        var langs = languages.ToList();
        var groups = new List<GroupForm>();
        for (var g = 0; g < MaxGroups * 3 && form.ContainsKey(Field(g, "name")); g++)
        {
            var group = new GroupForm
            {
                Name = form[Field(g, "name")].ToString().Trim(),
                Required = form[Field(g, "required")].ToString() == "true",
                Max = form[Field(g, "max")].ToString().Trim(),
                Translations = langs.ToDictionary(l => l, l => form[Field(g, $"tr-{l}")].ToString().Trim())
            };
            for (var o = 0; o < MaxOptionsPerGroup * 3 && form.ContainsKey(OptionField(g, o, "name")); o++)
            {
                group.Options.Add(new OptionForm
                {
                    Name = form[OptionField(g, o, "name")].ToString().Trim(),
                    Price = form[OptionField(g, o, "price")].ToString().Trim(),
                    Translations = langs.ToDictionary(l => l, l => form[OptionField(g, o, $"tr-{l}")].ToString().Trim())
                });
            }
            groups.Add(group);
        }
        return groups;
    }

    private static string? TranslationsJson(Dictionary<string, string> t)
    {
        var filled = t.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        return filled.Count == 0 ? null : JsonSerializer.Serialize(filled);
    }

    /// <summary>
    /// Turns the repeater into entities. Completely empty rows are ignored; everything else
    /// must be valid, and each problem gets one message the owner can act on.
    /// </summary>
    public static (List<ProductOptionGroup> Groups, List<string> Errors) Validate(IReadOnlyList<GroupForm> form)
    {
        var groups = new List<ProductOptionGroup>();
        var errors = new List<string>();

        var used = form.Where(g => g.Name.Length > 0 || g.Options.Any(o => o.Name.Length > 0 || o.Price.Length > 0)).ToList();
        if (used.Count > MaxGroups) errors.Add($"A dish can have at most {MaxGroups} option groups.");

        for (var i = 0; i < used.Count && i < MaxGroups; i++)
        {
            var g = used[i];
            var label = g.Name.Length > 0 ? $"\"{g.Name}\"" : $"Option group {i + 1}";
            if (g.Name.Length == 0) errors.Add($"{label}: give the group a name, like Size or Extras.");
            if (g.Name.Length > MaxNameLength) errors.Add($"{label}: the name can be at most {MaxNameLength} characters.");

            var options = new List<ProductOption>();
            var rows = g.Options.Where(o => o.Name.Length > 0 || o.Price.Length > 0).ToList();
            if (rows.Count == 0) errors.Add($"{label}: add at least one option.");
            if (rows.Count > MaxOptionsPerGroup) errors.Add($"{label}: at most {MaxOptionsPerGroup} options.");

            foreach (var (row, n) in rows.Take(MaxOptionsPerGroup).Select((r, n) => (r, n)))
            {
                if (row.Name.Length == 0) { errors.Add($"{label}: every option needs a name."); continue; }
                if (row.Name.Length > MaxNameLength) { errors.Add($"{label}: \"{row.Name}\" is longer than {MaxNameLength} characters."); continue; }
                var delta = 0m;
                if (row.Price.Length > 0
                    && (!decimal.TryParse(row.Price.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out delta)
                        || Math.Abs(delta) > MaxDelta || decimal.Round(delta, 2) != delta))
                {
                    errors.Add($"{label}: the price for \"{row.Name}\" isn't a valid amount (like 1.50, or -1 for cheaper).");
                    continue;
                }
                options.Add(new ProductOption
                {
                    Name = row.Name,
                    PriceDelta = delta,
                    DisplayOrder = n,
                    NameTranslations = TranslationsJson(row.Translations)
                });
            }

            if (options.Select(o => o.Name.ToLowerInvariant()).Distinct().Count() != options.Count)
            {
                errors.Add($"{label}: two options have the same name.");
            }

            if (!int.TryParse(g.Max, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) || max < 1)
            {
                errors.Add($"{label}: \"choose up to\" must be 1 or more.");
                max = 1;
            }

            groups.Add(new ProductOptionGroup
            {
                Name = g.Name,
                MinSelect = g.Required ? 1 : 0,
                // More than the number of options means "as many as they like".
                MaxSelect = Math.Max(1, Math.Min(max, Math.Max(options.Count, 1))),
                DisplayOrder = i,
                NameTranslations = TranslationsJson(g.Translations),
                Options = options
            });
        }

        var names = groups.Where(x => x.Name.Length > 0).Select(x => x.Name.ToLowerInvariant()).ToList();
        if (names.Distinct().Count() != names.Count) errors.Add("Two option groups have the same name.");

        return (errors.Count == 0 ? groups : new List<ProductOptionGroup>(), errors);
    }

    // ---------------------------------------------------------------- guest menu

    /// <summary>
    /// The lowest price a guest can pay: base price plus, for each required group, the
    /// cheapest option. Null when the options never lower or raise the minimum.
    /// </summary>
    public static decimal? FromPrice(Product p)
    {
        var groups = p.OptionGroups?.Where(g => g.MinSelect > 0 && g.Options.Any()).ToList();
        if (groups == null || groups.Count == 0) return null;
        var min = p.Price + groups.Sum(g => g.Options.OrderBy(o => o.PriceDelta).Take(g.MinSelect).Sum(o => o.PriceDelta));
        var anyVaries = groups.Any(g => g.Options.Select(o => o.PriceDelta).Distinct().Count() > 1 || g.Options.Any(o => o.PriceDelta != 0));
        return anyVaries ? min : null;
    }

    /// <summary>JSON for the dish sheet: groups with translated names and price deltas.</summary>
    public static string ClientJson(Product p, string language)
    {
        var groups = (p.OptionGroups ?? Enumerable.Empty<ProductOptionGroup>())
            .Where(g => g.Options.Any())
            .OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id)
            .Select(g => new
            {
                id = g.Id,
                name = TranslationHelper.GetTranslation(g.Name, g.NameTranslations, language),
                min = g.MinSelect,
                max = g.MaxSelect,
                options = g.Options.OrderBy(o => o.DisplayOrder).ThenBy(o => o.Id).Select(o => new
                {
                    id = o.Id,
                    name = TranslationHelper.GetTranslation(o.Name, o.NameTranslations, language),
                    delta = o.PriceDelta
                })
            });
        return JsonSerializer.Serialize(groups);
    }

    public static bool HasOptions(Product p) => p.OptionGroups?.Any(g => g.Options.Any()) == true;

    public record Words(string Required, string Optional, string UpTo, string PleaseChoose, string From);

    private static readonly Dictionary<string, Words> GuestWords = new()
    {
        ["en"] = new("Required", "Optional", "up to {0}", "Please choose: {0}", "from {0}"),
        ["sq"] = new("E detyrueshme", "Opsionale", "deri në {0}", "Ju lutemi zgjidhni: {0}", "nga {0}"),
        ["it"] = new("Obbligatorio", "Facoltativo", "fino a {0}", "Scegli: {0}", "da {0}"),
        ["de"] = new("Pflicht", "Optional", "bis zu {0}", "Bitte wählen: {0}", "ab {0}"),
        ["fr"] = new("Obligatoire", "Facultatif", "jusqu'à {0}", "Veuillez choisir : {0}", "dès {0}"),
        ["es"] = new("Obligatorio", "Opcional", "hasta {0}", "Elige: {0}", "desde {0}"),
        ["tr"] = new("Zorunlu", "İsteğe bağlı", "en fazla {0}", "Lütfen seçin: {0}", "{0} ve üzeri")
    };

    public static Words For(string? language) => GuestWords.TryGetValue(language ?? "en", out var w) ? w : GuestWords["en"];
}
