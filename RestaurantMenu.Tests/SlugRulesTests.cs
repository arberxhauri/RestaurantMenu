namespace RestaurantMenu.Tests;

public class SlugRulesTests
{
    [Theory]
    [InlineData("Oliver's Italian", "olivers-italian")]
    [InlineData("Oliver’s Italian", "olivers-italian")]
    [InlineData("Tirana Palace", "tirana-palace")]
    [InlineData("  Oliva   Kitchen  ", "oliva-kitchen")]
    [InlineData("OlivaKitchen", "olivakitchen")]
    [InlineData("Çajtore Ëmbëlsira", "cajtore-embelsira")]
    [InlineData("Byrek & Kos", "byrek-kos")]
    [InlineData("Café 2000", "cafe-2000")]
    [InlineData("Straße", "strasse")]
    [InlineData("Şişli Köfte", "sisli-kofte")]
    [InlineData("Łódź Bar", "lodz-bar")]
    [InlineData("Pizza/Pasta?#%", "pizza-pasta")]
    [InlineData("--Bar--", "bar")]
    public void Names_become_lowercase_ascii_with_single_hyphens(string name, string slug)
    {
        Assert.Equal(slug, SlugRules.FromName(name));
    }

    [Theory]
    [InlineData("Ku", "ku-menu")]
    [InlineData("A", "a-menu")]
    [InlineData("壽司", SlugRules.Fallback)]
    [InlineData("", SlugRules.Fallback)]
    [InlineData(null, SlugRules.Fallback)]
    [InlineData("!!!", SlugRules.Fallback)]
    public void Short_or_unwritable_names_still_get_a_valid_slug(string? name, string slug)
    {
        Assert.Equal(slug, SlugRules.FromName(name));
        Assert.True(SlugRules.IsValid(slug));
    }

    [Fact]
    public void Long_names_are_cut_to_60_without_a_trailing_hyphen()
    {
        var slug = SlugRules.FromName(new string('a', 59) + " bcd");
        Assert.Equal(new string('a', 59), slug);
        Assert.True(SlugRules.IsValid(slug));
    }

    [Fact]
    public void Two_names_with_the_same_slug_get_different_links()
    {
        var taken = new HashSet<string>();
        var first = SlugRules.Unique("Oliva Kitchen", taken);
        taken.Add(first);
        var second = SlugRules.Unique("Oliva-Kitchen", taken);
        taken.Add(second);
        var third = SlugRules.Unique("OLIVA KITCHEN", taken);

        Assert.Equal("oliva-kitchen", first);
        Assert.Equal("oliva-kitchen-2", second);
        Assert.Equal("oliva-kitchen-3", third);
    }

    [Fact]
    public void The_old_links_of_the_two_colliding_names_differ_from_their_new_ones()
    {
        // Before slugs were stored both opened /menu/olivakitchen, whichever the database returned.
        Assert.Equal(SlugRules.Legacy("Oliva Kitchen"), SlugRules.Legacy("OlivaKitchen"));
        Assert.NotEqual(SlugRules.FromName("Oliva Kitchen"), SlugRules.FromName("OlivaKitchen"));
    }

    [Fact]
    public void Suffixes_keep_long_slugs_within_60()
    {
        var baseSlug = SlugRules.FromName(new string('x', 80));
        var taken = new HashSet<string> { baseSlug };
        var next = SlugRules.Unique(new string('x', 80), taken);
        Assert.Equal(SlugRules.MaxLength, next.Length);
        Assert.EndsWith("-2", next);
        Assert.True(SlugRules.IsValid(next));
    }

    [Theory]
    [InlineData("Order", "order-2")]
    [InlineData("Admin", "admin-2")]
    [InlineData("WWW", "www-2")]
    [InlineData("Feedback", "feedback-2")]
    public void Reserved_words_are_never_a_link(string name, string slug)
    {
        Assert.Equal(slug, SlugRules.Unique(name, new HashSet<string>()));
    }

    [Theory]
    [InlineData("olivers-italian", true)]
    [InlineData("ab", false)]
    [InlineData("-bar", false)]
    [InlineData("bar-", false)]
    [InlineData("bar--x", false)]
    [InlineData("Bar", false)]
    [InlineData("oliver'sitalian", false)]
    [InlineData("menu", false)]
    [InlineData(null, false)]
    public void Validity(string? slug, bool valid)
    {
        Assert.Equal(valid, SlugRules.IsValid(slug));
    }

    [Theory]
    [InlineData("oliver%27sitalian", "oliver'sitalian")]
    [InlineData("Oliver'sItalian", "oliver'sitalian")]
    [InlineData(" Tirana-Palace ", "tirana-palace")]
    [InlineData("%E", "%e")]
    [InlineData(null, "")]
    public void Request_segments_are_compared_decoded_and_lowercased(string? segment, string key)
    {
        Assert.Equal(key, SlugRules.Key(segment));
    }

    [Fact]
    public void The_legacy_link_is_the_name_without_spaces_lowercased()
    {
        Assert.Equal("oliver'sitalian", SlugRules.Legacy("Oliver's Italian"));
        Assert.Equal("tiranapalace", SlugRules.Legacy("Tirana Palace"));
    }

    [Fact]
    public void Every_reserved_word_is_lowercase_ascii()
    {
        Assert.All(SlugRules.Reserved, w => Assert.Matches("^[a-z0-9-]+$", w));
    }

    [Fact]
    public void Every_controller_name_is_reserved()
    {
        // A new section of the app must not be a restaurant's subdomain (kitchen.myquickmenu.al).
        var controllers = typeof(SlugRules).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.Name.EndsWith("Controller") && t.Name != "Controller")
            .Select(t => t.Name[..^"Controller".Length].ToLowerInvariant())
            .ToList();
        Assert.NotEmpty(controllers);
        Assert.All(controllers, c => Assert.Contains(c, SlugRules.Reserved));
    }
}
