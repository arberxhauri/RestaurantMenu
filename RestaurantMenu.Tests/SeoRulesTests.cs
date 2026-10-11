using Microsoft.Extensions.Configuration;
using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>Languages, generated meta copy, robots.txt and the Search &amp; sharing input rules.</summary>
public class SeoRulesTests
{
    private static Branch B(string languages, string? main = null) =>
        new() { Name = "Oliva", Slug = "oliva", SupportedLanguages = languages, DefaultLanguage = main, Address = "", PhoneNumber = "", UserId = "" };

    // ------------------------------------------------------------------ languages

    [Theory]
    [InlineData("en,sq", null, "en")]
    [InlineData("sq,en", null, "en")]
    [InlineData("sq", null, "sq")]
    [InlineData("sq,it", null, "sq")]
    [InlineData("en,sq", "sq", "sq")]
    [InlineData("en,sq", "it", "en")]      // a main language no longer offered falls back
    [InlineData("", null, "en")]
    public void The_main_language_is_the_owners_choice_else_english_else_the_first(string languages, string? main, string expected)
    {
        Assert.Equal(expected, SeoLanguages.Default(B(languages, main)));
    }

    [Theory]
    [InlineData(null, "en", false)]
    [InlineData("sq", "sq", false)]
    [InlineData("en", "en", true)]     // ?lang=main is a duplicate of the bare URL
    [InlineData("xx", "en", true)]     // a language the page doesn't have
    [InlineData("de", "en", true)]
    public void Lang_parameters_resolve_or_redirect(string? requested, string language, bool redirect)
    {
        Assert.Equal((language, redirect), SeoLanguages.Resolve(new[] { "en", "sq" }, "en", requested));
    }

    [Fact]
    public void The_base_text_language_is_english_when_offered()
    {
        Assert.Equal("en", SeoLanguages.BaseTextLanguage(B("sq,en", "sq")));
        Assert.Equal("sq", SeoLanguages.BaseTextLanguage(B("sq")));
    }

    [Theory]
    [InlineData("sq", "sq_AL")]
    [InlineData("en", "en_US")]
    [InlineData("it", "it_IT")]
    public void Og_locales_use_language_and_territory(string language, string locale)
    {
        Assert.Equal(locale, SeoLanguages.OgLocale(language));
    }

    [Fact]
    public void Redirect_query_keeps_the_table_and_drops_lang_and_tracking()
    {
        var query = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["lang"] = "en", ["t"] = "4", ["k"] = "AB12", ["utm_source"] = "chatgpt.com", ["fbclid"] = "x"
        });
        Assert.Equal("?t=4&k=AB12", SeoLanguages.QueryWithout(query));
    }

    [Fact]
    public void Menu_urls_put_only_non_main_languages_in_the_query()
    {
        var seo = new SeoService(new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Seo:BaseUrl"] = "https://myquickmenu.com" }).Build());
        var b = B("sq,en", "sq");
        Assert.Equal("https://myquickmenu.com/menu/oliva", seo.MenuUrl(b, "sq"));
        Assert.Equal("https://myquickmenu.com/menu/oliva?lang=en", seo.MenuUrl(b, "en"));
        Assert.Equal("https://myquickmenu.com/menu/oliva?t=3&k=X1", seo.MenuUrl(b, "sq", 3, "X1"));
    }

    // ------------------------------------------------------------------ meta copy

    [Theory]
    [InlineData("Test")]
    [InlineData("Testing")]
    [InlineData("test 2")]
    [InlineData("Test dishes")]
    [InlineData("asdf")]
    [InlineData("Lorem ipsum")]
    [InlineData("demo")]
    [InlineData("   ")]
    [InlineData("123")]
    public void Placeholder_names_are_recognised(string name) => Assert.True(SeoText.IsPlaceholder(name));

    [Theory]
    [InlineData("Starters")]
    [InlineData("Tasting menu")]
    [InlineData("Pjata kryesore")]
    [InlineData("Byrek")]
    [InlineData("Testarossa pizza")]
    public void Real_names_are_kept(string name) => Assert.False(SeoText.IsPlaceholder(name));

    [Fact]
    public void Menu_descriptions_skip_test_categories_and_name_only_real_features()
    {
        var text = SeoText.MenuDescription(new SeoText.MenuFacts("Oliver's Italian", "Tirana",
            new[] { "Testing", "Starters", "Pasta", "Pizza", "Desserts" }, HasPhotos: true, HasAllergens: false, HasDiets: false), "en");
        Assert.Equal("See the full menu of Oliver's Italian in Tirana: Starters, Pasta, Pizza. Prices and photos, updated by the restaurant.", text);
        Assert.DoesNotContain("Testing", text);
    }

    [Fact]
    public void Albanian_menu_descriptions_are_in_albanian()
    {
        var text = SeoText.MenuDescription(new SeoText.MenuFacts("Oliva", "Tiranë", new[] { "Pica", "Sallata" }, true, true, true), "sq");
        Assert.StartsWith("Menuja e plotë e Oliva, Tiranë: Pica, Sallata.", text);
        Assert.Contains("alergjenët", text);
        Assert.DoesNotContain("menu of", text);
    }

    [Fact]
    public void Descriptions_never_exceed_160_or_cut_a_word()
    {
        var categories = Enumerable.Range(1, 3).Select(i => "Extraordinarily long category name number " + i).ToList();
        var text = SeoText.MenuDescription(new SeoText.MenuFacts("A restaurant with a rather long name indeed", "Tirana", categories, true, true, true, 4), "en");
        Assert.True(text.Length <= SeoText.DescriptionMax, text);
        Assert.EndsWith(".", text);
    }

    [Fact]
    public void Trim_cuts_at_a_word_with_an_ellipsis()
    {
        var text = SeoText.Trim("We cook seasonal Albanian food by the sea, with fish from the morning boats and vegetables from our garden in Kruja.", 60);
        Assert.True(text.Length <= 60);
        Assert.EndsWith("…", text);
        Assert.DoesNotContain(" …", text);
        Assert.Equal("Short text", SeoText.Trim("  Short\n text ", 60));
    }

    [Theory]
    [InlineData("Oliva", "Tirana", "en", "Oliva Menu & Prices · Tirana")]
    [InlineData("Oliva", null, "sq", "Oliva: menuja dhe çmimet")]
    [InlineData("Oliva", "Tirana", "it", "Oliva: menu e prezzi · Tirana")]
    public void Menu_titles_follow_the_template(string name, string? city, string language, string expected)
    {
        Assert.Equal(expected, SeoText.MenuTitle(name, city, language));
    }

    [Fact]
    public void Long_menu_titles_drop_the_city_first()
    {
        var title = SeoText.MenuTitle("The Grand Seafood House by the Old Harbour", "Durrës", "en");
        Assert.Equal("The Grand Seafood House by the Old Harbour Menu & Prices", title);
        Assert.True(title.Length <= 60);
    }

    [Fact]
    public void Site_titles_say_what_the_place_is_in_the_page_language()
    {
        Assert.Equal("Oliva · Italian restaurant · Tirana", SeoText.SiteTitle("Oliva", BusinessType.Restaurant, "italian", "Tirana", "en"));
        Assert.Equal("Oliva · Restorant italian · Tiranë", SeoText.SiteTitle("Oliva", BusinessType.Restaurant, "italian", "Tiranë", "sq"));
        Assert.Equal("Kafe Flora · Kafene", SeoText.SiteTitle("Kafe Flora", BusinessType.Cafe, "italian", null, "sq"));
    }

    [Fact]
    public void Owner_text_is_used_only_in_its_own_language()
    {
        var translations = "{\"sq\":\"Pica me dru në Tiranë\"}";
        Assert.Equal("Wood-fired pizza in Tirana", SeoText.Own("Wood-fired pizza in Tirana", translations, "en", "en"));
        Assert.Equal("Pica me dru në Tiranë", SeoText.Own("Wood-fired pizza in Tirana", translations, "sq", "en"));
        Assert.Null(SeoText.Own("Wood-fired pizza in Tirana", translations, "it", "en"));   // generated Italian instead
        Assert.Null(SeoText.Own(null, null, "en", "en"));
    }

    // ------------------------------------------------------------------ robots.txt

    [Fact]
    public void Robots_welcomes_ai_crawlers_and_keeps_app_areas_closed_to_them_too()
    {
        var txt = RobotsTxt.Build("https://myquickmenu.com/sitemap.xml", RobotsTxt.AppOnlyPaths);
        foreach (var agent in new[] { "GPTBot", "OAI-SearchBot", "ChatGPT-User", "PerplexityBot", "Perplexity-User", "ClaudeBot", "Claude-SearchBot", "Claude-User", "Google-Extended", "Applebot-Extended", "Bingbot" })
            Assert.Contains($"User-agent: {agent}\n", txt);
        var aiGroup = txt[txt.IndexOf("User-agent: GPTBot", StringComparison.Ordinal)..];
        Assert.Contains("Disallow: /admin/", aiGroup);
        Assert.Contains("Disallow: /dashboard/", aiGroup);
        Assert.EndsWith("Sitemap: https://myquickmenu.com/sitemap.xml\n", txt);
    }

    [Fact]
    public void Restaurant_domains_get_their_own_sitemap_line()
    {
        var txt = RobotsTxt.Build("https://www.oliva.al/sitemap.xml", Array.Empty<string>());
        Assert.Contains("Sitemap: https://www.oliva.al/sitemap.xml", txt);
        Assert.DoesNotContain("Disallow:", txt);
    }

    // ------------------------------------------------------------------ validators

    [Fact]
    public void Etag_ignores_the_render_time_only()
    {
        var a = System.Text.Encoding.UTF8.GetBytes("<body data-rendered-at=\"2026-10-11T10:55:40Z\"><p>€5</p></body>");
        var b = System.Text.Encoding.UTF8.GetBytes("<body data-rendered-at=\"2026-10-11T10:59:01Z\"><p>€5</p></body>");
        var c = System.Text.Encoding.UTF8.GetBytes("<body data-rendered-at=\"2026-10-11T10:59:01Z\"><p>€6</p></body>");
        Assert.Equal(HtmlValidatorsMiddleware.ETagFor(a), HtmlValidatorsMiddleware.ETagFor(b));
        Assert.NotEqual(HtmlValidatorsMiddleware.ETagFor(b), HtmlValidatorsMiddleware.ETagFor(c));
    }

    // ------------------------------------------------------------------ Search & sharing input

    [Theory]
    [InlineData("41.3275", "19.8187", 41.3275, 19.8187)]
    [InlineData("41.3275, 19.8187", "", 41.3275, 19.8187)]   // pasted from Google Maps into one box
    [InlineData(" 41.32751234 ", "19.81871234", 41.327512, 19.818712)]
    public void Coordinates_are_parsed(string lat, string lng, double expectedLat, double expectedLng)
    {
        var (la, lo, error) = SearchRules.Coordinates(lat, lng);
        Assert.Null(error);
        Assert.Equal(expectedLat, la);
        Assert.Equal(expectedLng, lo);
    }

    [Theory]
    [InlineData("91", "19")]
    [InlineData("abc", "19")]
    [InlineData("41,3275", "")]       // a decimal comma: longitude 3275 is out of range
    public void Bad_coordinates_are_explained(string lat, string lng) => Assert.NotNull(SearchRules.Coordinates(lat, lng).Error);

    [Fact]
    public void No_coordinates_is_fine() => Assert.Equal((null, null, null), SearchRules.Coordinates("", " "));

    [Theory]
    [InlineData("https://maps.app.goo.gl/AbC123", true)]
    [InlineData("g.page/oliva-tirana", true)]
    [InlineData("https://www.google.com/maps/place/Oliva", true)]
    [InlineData("https://www.google.al/maps/place/Oliva", true)]
    [InlineData("https://evil.example/maps.app.goo.gl", false)]
    [InlineData("https://googlemaps.example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Google_business_links_must_be_google(string input, bool ok) =>
        Assert.Equal(ok, SearchRules.GoogleBusinessUrl(input) != null);

    [Fact]
    public void Checklist_scores_what_can_be_checked()
    {
        var b = B("en");
        b.Address = "Rruga e Kavajës 10";
        var list = ProfileChecklist.For(b, dishes: 10, dishPhotos: 6, site: null, hasOwnDomain: false, siteOn: false);
        Assert.InRange(list.Score, 1, 99);
        Assert.Contains(list.Items, i => i.Key == "address" && i.Done);
        Assert.Contains(list.Items, i => i.Key == "photos" && i.Done);
        Assert.Contains(list.Items, i => i.Key == "bio" && !i.Scored);
    }
}
