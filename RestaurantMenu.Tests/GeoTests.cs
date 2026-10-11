using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>llms.txt, AI referral detection and IndexNow settings.</summary>
public class GeoTests
{
    private static Dictionary<string, string> Tokens(string lang) => new()
    {
        ["definition"] = string.Format(MarketingText.For(lang).Definition, "€5"),
        ["menu"] = "€5", ["guests"] = "€23", ["everything"] = "€43", ["website"] = "€5", ["bookings"] = "€7",
        ["ordering"] = "€9", ["management"] = "€12", ["domain"] = "€3", ["trial"] = "14", ["payment"] = "By bank transfer against an invoice, monthly or yearly."
    };

    private static LlmsText.Input Input() => new("https://myquickmenu.com",
        new BrandOptions { Phone = "+355 69 633 7906" }, Tokens("en"), Tokens("sq"), 12);

    [Fact]
    public void Llms_txt_follows_the_format_with_current_prices()
    {
        var txt = LlmsText.Summary(Input());
        var lines = txt.Split('\n');
        Assert.Equal("# My Quick Menu", lines[0]);
        Assert.StartsWith("> My Quick Menu is a restaurant platform from Tirana, Albania", lines[2]);
        foreach (var section in new[] { "## Product pages", "## Pricing", "## FAQ", "## About and contact", "## Restaurant directory", "## Shqip (Albanian)", "## Optional" })
            Assert.Contains(section + "\n", txt);
        Assert.Contains("Digital menu €5", txt);
        Assert.Contains("Everything €43", txt);
        Assert.Contains("(https://myquickmenu.com/digital-menu)", txt);
        Assert.Contains("(https://myquickmenu.com/digital-menu?lang=sq)", txt);
        Assert.Contains("12 restaurants", txt);
        Assert.Contains("info@myquickmenu.com, +355 69 633 7906", txt);
        Assert.DoesNotContain("{", txt);
    }

    [Fact]
    public void Llms_full_has_every_page_in_both_languages_without_markup()
    {
        var txt = LlmsText.Full(Input());
        Assert.Contains("# English", txt);
        Assert.Contains("# Shqip", txt);
        Assert.Contains("## QR code menu for restaurants", txt);
        Assert.Contains("## Menu dixhitale me QR për restorante", txt);
        Assert.Contains("Q: How much does My Quick Menu cost?", txt);
        Assert.Contains("| My Quick Menu | Printed menu | PDF menu |", txt);
        Assert.DoesNotContain("](/", txt);         // links written out as "label (url)"
        Assert.DoesNotContain("{menu}", txt);
    }

    [Theory]
    [InlineData("https://chatgpt.com/", null, "ChatGPT")]
    [InlineData("https://chat.openai.com/c/123", null, "ChatGPT")]
    [InlineData("https://www.perplexity.ai/search?q=menu", null, "Perplexity")]
    [InlineData("https://claude.ai/chat/abc", null, "Claude")]
    [InlineData("https://gemini.google.com/app", null, "Gemini")]
    [InlineData("https://copilot.microsoft.com/", null, "Copilot")]
    [InlineData(null, "chatgpt.com", "ChatGPT")]
    [InlineData("https://www.google.com/", "perplexity", "Perplexity")]
    public void Ai_assistants_are_recognised(string? referer, string? utm, string source) =>
        Assert.Equal(source, AiReferrals.Detect(referer, utm)?.Source);

    [Theory]
    [InlineData("https://www.google.com/search?q=chatgpt.com", null)]
    [InlineData("https://notchatgpt.com/", null)]
    [InlineData("https://claude.ai.evil.example/", null)]
    [InlineData(null, "newsletter")]
    [InlineData("not a url", "")]
    public void Other_visits_are_not(string? referer, string? utm) =>
        Assert.Null(AiReferrals.Detect(referer, utm));

    [Theory]
    [InlineData("abcdef12-3456-7890", true)]
    [InlineData("short", false)]
    [InlineData("has space here", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData(null, false)]
    public void IndexNow_keys_must_be_valid(string? key, bool enabled) =>
        Assert.Equal(enabled, new IndexNowOptions { Key = key }.Enabled);
}
