using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RestaurantMenu.Tests;

/// <summary>
/// Pages load only the Phosphor icons the app uses (wwwroot/lib/phosphor/icons.css). Using a new
/// icon without regenerating the subset would show an empty square: run tools/icons/subset.py.
/// </summary>
public class IconSubsetTests
{
    [Fact]
    public void Every_icon_in_the_code_is_in_the_subset()
    {
        var app = Path.Combine(Here(), "..", "RestaurantMenu");
        var css = File.ReadAllText(Path.Combine(app, "wwwroot", "lib", "phosphor", "icons.css"));
        var used = new[] { "Views", Path.Combine("wwwroot", "js"), "Helpers", "Controllers", "Services" }
            .SelectMany(f => Directory.EnumerateFiles(Path.Combine(app, f), "*.*", SearchOption.AllDirectories))
            .Where(f => !Path.GetFileName(f).StartsWith("._") && (f.EndsWith(".cshtml") || f.EndsWith(".js") || f.EndsWith(".cs")))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"ph-[a-z0-9]+(?:-[a-z0-9]+)*").Select(m => m.Value))
            .Where(n => n != "ph-fill")
            .ToHashSet();
        var missing = used.Where(n => !css.Contains($".ph.{n}:before")).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, "Not in icons.css (run tools/icons/subset.py): " + string.Join(", ", missing));
    }

    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
