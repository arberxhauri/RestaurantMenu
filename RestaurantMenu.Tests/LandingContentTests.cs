using System.Text.RegularExpressions;
using RestaurantMenu.Helpers;
using RestaurantMenu.Helpers.Landing;

namespace RestaurantMenu.Tests;

/// <summary>The marketing content pages: both languages complete, meta lengths, tokens and links.</summary>
public class LandingContentTests
{
    // Realistic worst-case values: the longest prices and trial a page could show.
    private static readonly Dictionary<string, string> Tokens = new()
    {
        ["definition"] = string.Format(MarketingText.En.Definition, "€19.50"),
        ["menu"] = "€19.50", ["guests"] = "€31.50", ["everything"] = "€52.50", ["website"] = "€5", ["bookings"] = "€7",
        ["ordering"] = "€9", ["management"] = "€12", ["domain"] = "€3", ["trial"] = "14", ["payment"] = "By bank transfer against an invoice, monthly or yearly."
    };

    private static readonly string[] KnownPaths =
    {
        "/", "/pricing", "/digital-menu", "/restaurant-website", "/table-bookings", "/restaurant-management", "/faq", "/about", "/contact", "/restaurants"
    };

    public static IEnumerable<object[]> Pages() =>
        LandingContent.Keys.SelectMany(k => new[] { new object[] { k, "en" }, new object[] { k, "sq" } });

    [Theory]
    [MemberData(nameof(Pages))]
    public void Meta_fits_in_search_results(string key, string language)
    {
        var p = LandingContent.Get(key, language);
        var tokens = new Dictionary<string, string>(Tokens)
        {
            ["definition"] = string.Format(MarketingText.For(language).Definition, "€19.50")
        };
        var title = LandingContent.Fill(p.MetaTitle, tokens);
        var description = LandingContent.Fill(p.MetaDescription, tokens);
        Assert.InRange(title.Length, 20, 60);
        Assert.InRange(description.Length, 110, 160);
        Assert.Contains("My Quick Menu", title);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_token_is_known_and_every_link_is_a_real_page(string key, string language)
    {
        var p = LandingContent.Get(key, language);
        foreach (var text in AllText(p))
        {
            foreach (Match m in Regex.Matches(text, @"\{([a-z]+)\}"))
                Assert.True(Tokens.ContainsKey(m.Groups[1].Value), $"{key}/{language}: unknown token {m.Value}");
            foreach (Match m in Regex.Matches(text, @"\]\((/[^)#?]*)"))
                Assert.Contains(m.Groups[1].Value, KnownPaths);
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Pages_open_with_the_brand_definition_or_a_quotable_one(string key, string language)
    {
        var p = LandingContent.Get(key, language);
        if (key == "contact") return;   // a contact page opens with how to reach us
        Assert.Contains("{definition}", p.Lede + string.Join(" ", p.Faqs?.Select(f => f.Answer) ?? Array.Empty<string>()));
    }

    [Theory]
    [InlineData("digital-menu")]
    [InlineData("restaurant-website")]
    [InlineData("table-bookings")]
    [InlineData("restaurant-management")]
    public void Product_pages_have_everything_the_brief_asks_for(string key)
    {
        foreach (var language in new[] { "en", "sq" })
        {
            var p = LandingContent.Get(key, language);
            Assert.True(p.Sections.Count >= 3, $"{key}/{language} sections");
            Assert.True(p.Sections.Count(s => s.Heading.EndsWith('?')) >= 2, $"{key}/{language} question headings");
            Assert.InRange(p.Faqs!.Count, 4, 6);
            Assert.NotNull(p.Comparison);
            Assert.True(p.Features!.Count >= 4);
            Assert.True(p.Steps!.Count >= 3);
            Assert.NotEmpty(p.Screenshots!);
            Assert.All(p.Screenshots!, s => Assert.True(s.Alt.Length > 30, "descriptive alt text"));
            var words = AllText(p).Sum(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.True(words >= 550, $"{key}/{language}: {words} words");
        }
    }

    [Fact]
    public void Albanian_and_english_have_the_same_shape()
    {
        foreach (var key in LandingContent.Keys)
        {
            var en = LandingContent.Get(key, "en");
            var sq = LandingContent.Get(key, "sq");
            Assert.Equal(en.Sections.Count, sq.Sections.Count);
            Assert.Equal(en.Sections.Select(s => s.Id), sq.Sections.Select(s => s.Id));
            Assert.Equal(en.Faqs?.Count, sq.Faqs?.Count);
            Assert.Equal(en.Comparison?.Rows.Count, sq.Comparison?.Rows.Count);
            Assert.Equal(en.Screenshots?.Select(s => s.Image), sq.Screenshots?.Select(s => s.Image));
            Assert.NotEqual(en.H1, sq.H1);
        }
    }

    [Fact]
    public void The_brand_definition_is_word_for_word_the_same_in_both_languages_and_mentions_tirana()
    {
        Assert.StartsWith("My Quick Menu is a restaurant platform from Tirana, Albania", MarketingText.En.Definition);
        Assert.StartsWith("My Quick Menu është një platformë për restorante nga Tirana", MarketingText.Sq.Definition);
        Assert.Contains("{0}", MarketingText.En.Definition);
        Assert.Contains("{0}", MarketingText.Sq.Definition);
    }

    [Fact]
    public void Todo_notes_are_hidden_outside_development()
    {
        var before = LandingContent.ShowTodos;
        try
        {
            LandingContent.ShowTodos = false;
            Assert.Equal("Address: Tirana, Albania", LandingContent.Fill("Address: Tirana, Albania [TODO: street address]", Tokens));
            Assert.Equal("", LandingContent.Fill("[TODO: support hours.]", Tokens));
            LandingContent.ShowTodos = true;
            Assert.Contains("[TODO:", LandingContent.Fill("Address [TODO: x]", Tokens));
        }
        finally
        {
            LandingContent.ShowTodos = before;
        }
    }

    [Fact]
    public void Links_are_encoded_localized_and_only_to_safe_targets()
    {
        Assert.Equal("See <a href=\"/pricing?lang=sq\">çmimet</a> &amp; more", LandingContent.Html("See [çmimet](/pricing) & more", "sq"));
        Assert.Equal("<a href=\"/faq#cost\">x</a>", LandingContent.Html("[x](/faq#cost)", "en"));
        Assert.DoesNotContain("href", LandingContent.Html("[click](javascript:alert(1))"));
        Assert.Equal("&lt;script&gt;", LandingContent.Html("<script>"));
        Assert.Equal("/faq?lang=sq#cost", LandingContent.Localize("/faq#cost", "sq"));
    }

    [Fact]
    public void Faq_page_collects_every_question_once()
    {
        foreach (var language in new[] { "en", "sq" })
        {
            var groups = LandingContent.FaqGroups(language);
            Assert.Equal(5, groups.Count);
            var questions = groups.SelectMany(g => g.Faqs).Select(f => f.Question).ToList();
            Assert.Equal(questions.Count, questions.Distinct().Count());
        }
    }

    private static IEnumerable<string> AllText(LandingContent.Page p)
    {
        yield return p.Lede;
        foreach (var s in p.Sections)
        {
            yield return s.Heading;
            foreach (var x in s.Paragraphs) yield return x;
            foreach (var x in s.Bullets ?? Array.Empty<string>()) yield return x;
        }
        foreach (var f in p.Features ?? Array.Empty<LandingContent.Feature>()) { yield return f.Title; yield return f.Text; }
        foreach (var s in p.Steps ?? Array.Empty<LandingContent.Step>()) { yield return s.Title; yield return s.Text; }
        foreach (var f in p.Faqs ?? Array.Empty<LandingContent.Faq>()) { yield return f.Question; yield return f.Answer; }
        if (p.Comparison != null) foreach (var row in p.Comparison.Rows) foreach (var c in row) yield return c;
    }
}
