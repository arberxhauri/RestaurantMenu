namespace RestaurantMenu.Tests;

public class BrandThemeTests
{
    // 16 levels per channel = 4,096 colours, from #000000 to #FFFFFF.
    public static IEnumerable<string> Grid()
    {
        for (var r = 0; r < 16; r++)
        for (var g = 0; g < 16; g++)
        for (var b = 0; b < 16; b++)
            yield return $"#{r * 17:X2}{g * 17:X2}{b * 17:X2}";
    }

    [Fact]
    public void Contrast_of_black_on_white_is_21()
    {
        Assert.Equal(21, BrandTheme.Contrast("#000000", "#FFFFFF"), 3);
        Assert.Equal(1, BrandTheme.Contrast("#C8642A", "#C8642A"), 3);
    }

    [Fact]
    public void Text_on_any_colour_reaches_4_5_to_1()
    {
        foreach (var c in Grid())
        {
            Assert.True(BrandTheme.Contrast(BrandTheme.TextOn(c), c) >= 4.5, $"text on {c}");
        }
    }

    [Fact]
    public void Coloured_text_is_adjusted_to_read_on_both_menu_backgrounds()
    {
        foreach (var c in Grid())
        {
            Assert.True(BrandTheme.Contrast(BrandTheme.Readable(c, BrandTheme.LightBackground), BrandTheme.LightBackground) >= 4.5, $"{c} on light");
            Assert.True(BrandTheme.Contrast(BrandTheme.Readable(c, BrandTheme.DarkBackground), BrandTheme.DarkBackground) >= 4.5, $"{c} on dark");
        }
    }

    [Fact]
    public void Readable_colours_are_left_alone()
    {
        Assert.Equal("#14532D", BrandTheme.Readable("#14532d", BrandTheme.LightBackground));
    }

    [Theory]
    [InlineData("#c8642a", "#C8642A")]
    [InlineData("c8642a", "#C8642A")]
    [InlineData("#c62", "#CC6622")]
    [InlineData("red", null)]
    [InlineData("#12345", null)]
    [InlineData(null, null)]
    public void Hex_is_normalised(string? input, string? expected) => Assert.Equal(expected, BrandTheme.NormalizeHex(input));

    [Fact]
    public void Theme_survives_a_round_trip_and_bad_json()
    {
        var t = new BrandTheme { Primary = "#112233", Appearance = "dark", Header = "minimal" };
        var back = BrandTheme.Parse(t.ToJson());
        Assert.Equal("#112233", back.Primary);
        Assert.Equal("dark", back.Appearance);
        Assert.Equal("minimal", back.Header);

        Assert.Equal(BrandTheme.DefaultPrimary, BrandTheme.Parse("{not json").Primary);
        Assert.Equal(BrandTheme.DefaultPrimary, BrandTheme.Parse(null).Primary);
    }
}

public class StockRulesTests
{
    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("1,5", "1.5")]
    [InlineData(" 2 ", "2")]
    [InlineData("1,000", "1")]          // one comma, no dot: a decimal comma
    [InlineData("1,000,000", "1000000")] // several commas: thousands separators
    [InlineData("1,000.25", "1000.25")]
    [InlineData("1.2345", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData("2000000", null)]
    public void Amounts_are_parsed_strictly(string input, string? expected) =>
        Assert.Equal(expected == null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), StockRules.Parse(input));

    [Fact]
    public void Negative_amounts_only_when_allowed() => Assert.Equal(-1m, StockRules.Parse("-1", allowNegative: true));

    [Fact]
    public void Low_stock()
    {
        Assert.True(StockRules.IsLow(new Ingredient { Name = "Flour", Quantity = 0 }));
        Assert.True(StockRules.IsLow(new Ingredient { Name = "Flour", Quantity = 2, LowLevel = 2 }));
        Assert.False(StockRules.IsLow(new Ingredient { Name = "Flour", Quantity = 3, LowLevel = 2 }));
        Assert.False(StockRules.IsLow(new Ingredient { Name = "Flour", Quantity = 3 }));
    }

    [Fact]
    public void Format_trims_zeros() => Assert.Equal("1.5", StockRules.Format(1.500m));
}

public class FeedbackRulesTests
{
    [Theory]
    [InlineData("ChIJN1t_tDeuEmsRUsoyG83frY4", "https://search.google.com/local/writereview?placeid=ChIJN1t_tDeuEmsRUsoyG83frY4")]
    [InlineData("g.page/r/CabcDEF/review", "https://g.page/r/CabcDEF/review")]
    [InlineData("https://www.google.al/maps/place/Oliva", "https://www.google.al/maps/place/Oliva")]
    [InlineData("https://maps.app.goo.gl/abc123", "https://maps.app.goo.gl/abc123")]
    [InlineData("http://g.page/r/CabcDEF/review", null)]
    [InlineData("https://google.com.evil.com/review", null)]
    [InlineData("https://evil.com/?google.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void Only_google_review_links_are_accepted(string input, string? expected) =>
        Assert.Equal(expected, FeedbackRules.NormalizeGoogleReviewUrl(input));

    [Fact]
    public void Comments_keep_paragraphs_but_lose_control_characters()
    {
        Assert.Equal("Great food\n\nSlow service", FeedbackRules.Clean("Great food\r\n\r\n\r\n\r\nSlow\u0007 service".Replace("\u0007", ""), 1000));
        Assert.Equal("ab", FeedbackRules.Clean("ab\u0000", 1000));
        Assert.Equal("abc", FeedbackRules.Clean("abcdef", 3));
    }

    [Fact]
    public void Stars() => Assert.Equal("★★★☆☆", FeedbackRules.Stars(3));
}

public class SiteRulesTests
{
    [Theory]
    [InlineData("https://WWW.Example.al/menu", "www.example.al")]
    [InlineData("example.al:8080", "example.al")]
    [InlineData("example.al.", "example.al")]
    [InlineData("çajtore.al", "xn--ajtore-vua.al")]
    [InlineData("192.168.1.1", null)]
    [InlineData("localhost", null)]
    [InlineData("printer.local", null)]
    [InlineData("user@evil.com", null)]
    [InlineData("example", null)]
    [InlineData("-bad-.al", null)]
    public void Hosts_are_normalised(string input, string? expected) => Assert.Equal(expected, SiteRules.NormalizeHost(input));

    [Theory]
    [InlineData("example.al", true)]
    [InlineData("example.com.al", true)]
    [InlineData("www.example.al", false)]
    public void Apex_domains(string host, bool apex) => Assert.Equal(apex, SiteRules.IsApex(host));

    [Theory]
    [InlineData("@oliva.kitchen", "oliva.kitchen")]
    [InlineData("https://www.instagram.com/oliva_kitchen/", "oliva_kitchen")]
    [InlineData("not a handle!", null)]
    public void Instagram_handles(string input, string? expected) => Assert.Equal(expected, SiteRules.CleanInstagram(input));

    [Theory]
    [InlineData("facebook.com/oliva", "https://facebook.com/oliva")]
    [InlineData("https://evil.com/facebook.com", null)]
    public void Facebook_pages(string input, string? expected) => Assert.Equal(expected, SiteRules.CleanFacebook(input));
}
