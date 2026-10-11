using System.Runtime.CompilerServices;
using System.Text.Json;
using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>
/// JSON-LD per page type, compared with reviewed snapshots in Snapshots/. After an intended change,
/// run with UPDATE_SNAPSHOTS=1, review the diff of Snapshots/*.json, and commit it.
/// </summary>
public class StructuredDataTests
{
    private const string Base = "https://myquickmenu.com";

    // ------------------------------------------------------------------ fixtures

    private static Branch FullBranch()
    {
        var b = new Branch
        {
            Id = 7, Name = "Oliver's Italian & Co", Slug = "olivers-italian", Address = "Rruga Ismail Qemali 12", PhoneNumber = "+355 69 000 0000",
            Currency = "EUR", SupportedLanguages = "en,sq", DefaultLanguage = "en", UserId = "u1",
            Logo = "/images/logos/logo.png", Banner = "/images/banners/room.jpg",
            City = "Tirana", Cuisine = "italian", BusinessType = BusinessType.Restaurant,
            Latitude = 41.3275, Longitude = 19.8187, GoogleBusinessUrl = "https://maps.app.goo.gl/abc",
            HoursEnabled = true,
            OpeningHours = new List<BranchHours>
            {
                // Mon-Fri lunch and dinner, Saturday past midnight, Sunday 24 hours.
                H(DayOfWeek.Monday, 12, 15), H(DayOfWeek.Monday, 19, 23),
                H(DayOfWeek.Tuesday, 12, 15), H(DayOfWeek.Tuesday, 19, 23),
                H(DayOfWeek.Wednesday, 12, 15), H(DayOfWeek.Wednesday, 19, 23),
                H(DayOfWeek.Thursday, 12, 15), H(DayOfWeek.Thursday, 19, 23),
                H(DayOfWeek.Friday, 12, 15), H(DayOfWeek.Friday, 19, 23),
                H(DayOfWeek.Saturday, 18, 2),
                H(DayOfWeek.Sunday, 0, 0)
            }
        };
        b.Categories = new List<Category>
        {
            new()
            {
                Id = 1, Name = "Testing", Priority = 0, BranchId = 7,
                Products = new List<Product> { P(1, "Test dish", 1m) }
            },
            new()
            {
                Id = 2, Name = "Starters & Salads", NameTranslations = "{\"sq\":\"Antipasta dhe sallata\"}", Priority = 1, BranchId = 7,
                Products = new List<Product>
                {
                    P(2, "Burrata & tomatoes", 9.5m, "Apulian burrata, heritage tomatoes", "/images/products/burrata.jpg", Diet.Vegetarian | Diet.GlutenFree,
                        nameSq: "Burrata me domate"),
                    P(3, "Falafel", 7m, null, null, Diet.Vegan | Diet.Vegetarian | Diet.Halal | Diet.Spicy),
                    P(4, "Seasonal soup", 0m, "Ask your waiter")   // no price: no offer
                }
            },
            new()
            {
                Id = 3, Name = "Pizza", Priority = 2, BranchId = 7,
                Products = new List<Product>
                {
                    P(5, "Margherita \"D.O.P.\"", 11m, "San Marzano, fior di latte </script><script>alert(1)</script>", null, Diet.Vegetarian, available: false)
                }
            }
        };
        return b;
    }

    private static Branch BareCafe() => new()
    {
        Id = 9, Name = "Kafe Flora", Slug = "kafe-flora", Address = "", PhoneNumber = "", Currency = "ALL", SupportedLanguages = "sq",
        UserId = "u2", BusinessType = BusinessType.Cafe,
        Categories = new List<Category>
        {
            new() { Id = 10, Name = "Kafe", Priority = 0, Products = new List<Product> { P(11, "Espresso", 0m), P(12, "Makiato e madhe", 0m) } }
        }
    };

    private static BranchHours H(DayOfWeek d, int open, int close) => new() { DayOfWeek = d, Opens = new TimeOnly(open, 0), Closes = new TimeOnly(close, 0) };

    private static Product P(int id, string name, decimal price, string? description = null, string? image = null, Diet diets = Diet.None,
        bool available = true, string? nameSq = null) => new()
    {
        Id = id, Name = name, Description = description ?? "", Price = price, Image = image, Diets = diets, IsAvailable = available, DisplayOrder = id,
        NameTranslations = nameSq == null ? null : JsonSerializer.Serialize(new Dictionary<string, string> { ["sq"] = nameSq })
    };

    private static string Abs(string path) => path.StartsWith("http") ? path : Base + path;

    private static StructuredData.RestaurantFacts Facts(Branch b, string language, string? website, string? booking, IReadOnlyList<string>? sameAs = null)
    {
        var menu = $"{Base}/menu/{b.Slug}" + SeoLanguages.Query(language, SeoLanguages.Default(b));
        var home = website ?? $"{Base}/menu/{b.Slug}";
        return new StructuredData.RestaurantFacts(b, language, StructuredData.RestaurantId(home), home, menu, booking,
            About: website == null ? null : "Family-run trattoria since 1998.", Slogan: website == null ? null : "Pasta & pizza by the park",
            SameAs: sameAs ?? Array.Empty<string>(), Country: "AL", Absolute: Abs);
    }

    private static StructuredData.MarketingFacts Marketing(string language) => new(
        Base, language,
        language == "sq" ? string.Format(MarketingText.Sq.Definition, "€5") : string.Format(MarketingText.En.Definition, "€5"),
        new BrandOptions { Phone = "+355 69 633 7906", ParentSameAs = new() { "https://www.instagram.com/4cs.al/" } },
        "EUR",
        new[] { ("Menu", 500), ("Menu, website and bookings", 2300), ("Everything", 4300) });

    // ------------------------------------------------------------------ snapshots

    [Fact]
    public void Menu_page_full() =>
        Snapshot("menu-full", StructuredData.ForBranchMenu(
            Facts(FullBranch(), "en", null, $"{Base}/book/olivers-italian"), "Oliver's Italian & Co Menu & Prices · Tirana",
            new[] { ("My Quick Menu", Base + "/"), ("Oliver's Italian & Co", $"{Base}/menu/olivers-italian") }));

    [Fact]
    public void Menu_page_in_albanian_keeps_the_restaurant_id() =>
        Snapshot("menu-full-sq", StructuredData.ForBranchMenu(
            Facts(FullBranch(), "sq", null, null), "Oliver's Italian & Co: menuja dhe çmimet · Tirana",
            new[] { ("My Quick Menu", Base + "/?lang=sq"), ("Oliver's Italian & Co", $"{Base}/menu/olivers-italian?lang=sq") }));

    [Fact]
    public void Menu_page_bare_cafe_without_hours_address_or_prices() =>
        Snapshot("menu-bare", StructuredData.ForBranchMenu(
            Facts(BareCafe(), "sq", null, null), "Kafe Flora: menuja dhe çmimet",
            new[] { ("My Quick Menu", Base + "/"), ("Kafe Flora", $"{Base}/menu/kafe-flora") }));

    [Fact]
    public void Website_page() =>
        Snapshot("site", StructuredData.ForBranchSite(
            Facts(FullBranch(), "en", "https://www.olivers.al/", $"{Base}/book/olivers-italian", new[] { "https://www.instagram.com/olivers/" }),
            "https://www.olivers.al/", "Oliver's Italian & Co · Italian restaurant · Tirana"));

    [Fact]
    public void Home_page() =>
        Snapshot("home", StructuredData.ForMarketingPage(Marketing("en"), Base + "/", "My Quick Menu: QR menus, websites & bookings for restaurants", null, withSoftware: true));

    [Fact]
    public void Pricing_page_in_albanian() =>
        Snapshot("pricing-sq", StructuredData.ForMarketingPage(Marketing("sq"), Base + "/pricing?lang=sq", "Çmimet",
            new[] { ("My Quick Menu", Base + "/?lang=sq"), ("Çmimet", Base + "/pricing?lang=sq") }, withSoftware: true));

    // ------------------------------------------------------------------ properties that must hold

    [Fact]
    public void Names_are_the_real_characters_escaped_once()
    {
        var json = StructuredData.ForBranchMenu(Facts(FullBranch(), "en", null, null), "t", new[] { ("a", Base) });
        Assert.DoesNotContain("&#x27;", json);
        Assert.DoesNotContain("&amp;", json);
        Assert.DoesNotContain("</", json);                     // can't close the script element
        Assert.DoesNotContain("<script", json);
        var names = Nodes(json).Select(n => n.TryGetProperty("name", out var v) ? v.GetString() : null).ToList();
        Assert.Contains("Oliver's Italian & Co", names);       // the parsed value is the real name
    }

    [Fact]
    public void Test_data_and_unpriced_dishes_are_left_out()
    {
        var json = StructuredData.ForBranchMenu(Facts(FullBranch(), "en", null, null), "t", new[] { ("a", Base) });
        Assert.DoesNotContain("Testing", json);
        Assert.DoesNotContain("Test dish", json);
        var soup = Menu(json).GetProperty("hasMenuSection")[0].GetProperty("hasMenuItem").EnumerateArray()
            .First(i => i.GetProperty("name").GetString() == "Seasonal soup");
        Assert.False(soup.TryGetProperty("offers", out _));
    }

    [Fact]
    public void Menu_and_website_describe_the_same_restaurant()
    {
        var site = Facts(FullBranch(), "en", "https://www.olivers.al/", null);
        var menuJson = StructuredData.ForBranchMenu(site with { MenuUrl = $"{Base}/menu/olivers-italian" }, "t", new[] { ("a", Base) });
        var siteJson = StructuredData.ForBranchSite(site, "https://www.olivers.al/", "t");
        string Id(string json) => Nodes(json).First(n => n.GetProperty("@type").GetString() == "Restaurant").GetProperty("@id").GetString()!;
        Assert.Equal("https://www.olivers.al/#restaurant", Id(menuJson));
        Assert.Equal(Id(menuJson), Id(siteJson));
    }

    [Fact]
    public void Business_type_picks_the_schema_type()
    {
        var json = StructuredData.ForBranchMenu(Facts(BareCafe(), "sq", null, null), "t", new[] { ("a", Base) });
        Assert.Equal("CafeOrCoffeeShop", Nodes(json)[0].GetProperty("@type").GetString());
    }

    [Fact]
    public void Hours_group_days_with_the_same_times()
    {
        var hours = StructuredData.Hours(FullBranch())!;
        Assert.Equal(4, hours.Count);
        Assert.Equal(5, hours[0].DayOfWeek!.Count);                    // Mon-Fri 12-15
        Assert.Equal(("12:00", "15:00"), (hours[0].Opens, hours[0].Closes));
        Assert.Contains(hours, h => h.DayOfWeek!.Count == 1 && h.DayOfWeek[0] == "https://schema.org/Saturday" && h.Closes == "02:00");
        Assert.Contains(hours, h => h.DayOfWeek!.Count == 1 && h.DayOfWeek[0] == "https://schema.org/Sunday" && h.Opens == "00:00" && h.Closes == "23:59");
        Assert.Null(StructuredData.Hours(BareCafe()));
    }

    [Theory]
    [InlineData("EUR", "€7–€11")]
    [InlineData("ALL", "7–11 L")]
    [InlineData("GBP", "£7–£11")]
    public void Price_range_uses_the_menu_currency(string currency, string expected)
    {
        var b = FullBranch();
        b.Currency = currency;
        b.Categories = b.Categories!.Skip(1).ToList();   // without the 1.00 test dish
        b.Categories.First().Products = b.Categories.First().Products!.Where(p => p.Id != 2).ToList();
        Assert.Equal(expected, StructuredData.PriceRange(b));
    }

    [Fact]
    public void No_ratings_or_reviews_ever()
    {
        foreach (var json in new[]
                 {
                     StructuredData.ForBranchMenu(Facts(FullBranch(), "en", null, null), "t", new[] { ("a", Base) }),
                     StructuredData.ForBranchSite(Facts(FullBranch(), "en", "https://x.al/", null), "https://x.al/", "t")
                 })
        {
            Assert.DoesNotContain("aggregateRating", json);
            Assert.DoesNotContain("\"review", json);
        }
    }

    [Fact]
    public void Offers_carry_monthly_prices_from_the_price_book()
    {
        var json = StructuredData.ForMarketingPage(Marketing("en"), Base + "/", "t", null, withSoftware: true);
        var software = Nodes(json).First(n => n.GetProperty("@type").GetString() == "SoftwareApplication");
        var prices = software.GetProperty("offers").EnumerateArray().Select(o => o.GetProperty("price").GetString()).ToList();
        Assert.Equal(new[] { "5.00", "23.00", "43.00" }, prices);
        Assert.Equal("MONTH", software.GetProperty("offers")[0].GetProperty("priceSpecification").GetProperty("unitText").GetString());
    }

    // ------------------------------------------------------------------ helpers

    private static List<JsonElement> Nodes(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("@graph").EnumerateArray().ToList();

    private static JsonElement Menu(string json) => Nodes(json).First(n => n.GetProperty("@type").GetString() == "Menu");

    private static void Snapshot(string name, string json, [CallerFilePath] string here = "")
    {
        var pretty = JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement,
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var path = Path.Combine(Path.GetDirectoryName(here)!, "Snapshots", name + ".json");
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, pretty + "\n");
            Assert.Fail($"Snapshot {name}.json written; review it and run the tests again.");
        }
        Assert.Equal(File.ReadAllText(path).TrimEnd(), pretty.TrimEnd());
    }
}
