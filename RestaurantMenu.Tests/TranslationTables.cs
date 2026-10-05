using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RestaurantMenu.Tests;

/// <summary>
/// Finds every guest-facing word table in RestaurantMenu.Helpers (static dictionaries keyed by
/// language code) and flattens each language's entry into (key, text) pairs, so tests can check
/// coverage and the review sheet (docs/translations/) can be generated from the real strings.
/// </summary>
public static class TranslationTables
{
    public static readonly string[] Languages = { "en", "sq", "it", "de", "fr", "es", "tr" };

    public record Table(string Name, Dictionary<string, List<(string Key, string Text)>> ByLanguage);

    public static List<Table> All()
    {
        var tables = new List<Table>();
        var types = typeof(BrandTheme).Assembly.GetTypes().Where(t => t.Namespace == "RestaurantMenu.Helpers");
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.GetValue(null) is not IDictionary dict || field.FieldType.GetGenericArguments() is not [var keyType, _] || keyType != typeof(string)) continue;
                if (!dict.Contains("en") || !dict.Contains("sq")) continue;

                var byLang = new Dictionary<string, List<(string, string)>>();
                foreach (DictionaryEntry e in dict)
                {
                    byLang[(string)e.Key] = Flatten(e.Value).ToList();
                }
                tables.Add(new Table($"{type.Name}.{field.Name}", byLang));
            }
        }
        return tables.OrderBy(t => t.Name).ToList();
    }

    private static IEnumerable<(string Key, string Text)> Flatten(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string s:
                yield return ("", s);
                break;
            case string[] arr:
                for (var i = 0; i < arr.Length; i++) yield return ($"[{i}]", arr[i]);
                break;
            case ITuple tuple:
                for (var i = 0; i < tuple.Length; i++) yield return ($"Item{i + 1}", tuple[i]?.ToString() ?? "");
                break;
            default:
                foreach (var p in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                             .Where(p => p.PropertyType == typeof(string) && p.Name != "EqualityContract"))
                {
                    yield return (p.Name, (string?)p.GetValue(value) ?? "");
                }
                break;
        }
    }
}

public class TranslationCoverageTests
{
    [Fact]
    public void Word_tables_are_found()
    {
        // BookingText, DietaryText, FeedbackRules, Highlights, OfflineText, OpeningHours, OrderText,
        // ProductOptions, ServingTimes, SiteRules, TableText: losing one would hide gaps silently.
        Assert.True(TranslationTables.All().Count >= 11);
    }

    [Fact]
    public void Every_table_has_all_seven_languages_with_no_blanks()
    {
        var problems = new List<string>();
        foreach (var table in TranslationTables.All())
        {
            var englishKeys = table.ByLanguage["en"].Select(x => x.Key).ToList();
            foreach (var lang in TranslationTables.Languages)
            {
                if (!table.ByLanguage.TryGetValue(lang, out var entries))
                {
                    problems.Add($"{table.Name}: no {lang}");
                    continue;
                }
                if (!entries.Select(x => x.Key).SequenceEqual(englishKeys)) problems.Add($"{table.Name}: {lang} has different entries from en");
                problems.AddRange(entries.Where(x => string.IsNullOrWhiteSpace(x.Text)).Select(x => $"{table.Name}.{x.Key}: {lang} is empty"));
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Placeholders_match_english()
    {
        var problems = new List<string>();
        foreach (var table in TranslationTables.All())
        {
            var en = table.ByLanguage["en"].ToDictionary(x => x.Key, x => x.Text);
            foreach (var (lang, entries) in table.ByLanguage)
            {
                foreach (var (key, text) in entries)
                {
                    if (!en.TryGetValue(key, out var english)) continue;
                    var want = Placeholders(english);
                    var got = Placeholders(text);
                    if (!want.SetEquals(got)) problems.Add($"{table.Name}.{key} [{lang}]: {{{string.Join(",", got)}}} vs en {{{string.Join(",", want)}}}");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static HashSet<string> Placeholders(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).ToHashSet();
}
