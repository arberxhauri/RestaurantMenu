using System.Globalization;
using RestaurantMenu.Helpers.JsonLd;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Helpers;

/// <summary>
/// schema.org JSON-LD for every public page, as one @graph per page with stable @ids, so the same
/// organisation, website and restaurant are recognised as one entity wherever they appear:
///   https://myquickmenu.com/#organization, /#website, /#software (the marketing site)
///   {restaurant address}#restaurant (the same on its menu and its website)
/// Only what the page shows goes in. No aggregateRating or reviews from the "How was your visit?"
/// feedback: self-collected reviews on a business are not eligible and risk a manual action.
/// Pure functions over plain inputs; snapshot-tested in StructuredDataTests.
/// </summary>
public static class StructuredData
{
    // ------------------------------------------------------------------ marketing site

    /// <summary>What the marketing pages say about My Quick Menu. One instance per request.</summary>
    public record MarketingFacts(
        string BaseUrl,
        string Language,
        string Definition,
        BrandOptions Brand,
        string Currency,
        IReadOnlyList<(string Name, int MonthlyCents)> Plans);

    public static string OrganizationId(string baseUrl) => $"{baseUrl}/#organization";
    public static string WebSiteId(string baseUrl) => $"{baseUrl}/#website";
    public static string SoftwareId(string baseUrl) => $"{baseUrl}/#software";

    /// <summary>My Quick Menu, the company: on every marketing page.</summary>
    public static Organization Organization(MarketingFacts f)
    {
        var b = f.Brand;
        var parentSameAs = b.ParentSameAs.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return new Organization
        {
            Id = OrganizationId(f.BaseUrl),
            Name = b.Name,
            Url = f.BaseUrl + "/",
            Description = f.Definition,
            Logo = new ImageObject { Id = $"{f.BaseUrl}/#logo", Url = f.BaseUrl + "/logo.png", Width = 774, Height = 774 },
            Image = Thing.Ref($"{f.BaseUrl}/#logo"),
            Email = b.Email,
            Telephone = string.IsNullOrWhiteSpace(b.Phone) ? null : b.Phone,
            Address = new PostalAddress
            {
                StreetAddress = string.IsNullOrWhiteSpace(b.StreetAddress) ? null : b.StreetAddress,
                AddressLocality = b.Locality,
                PostalCode = string.IsNullOrWhiteSpace(b.PostalCode) ? null : b.PostalCode,
                AddressCountry = b.Country
            },
            AreaServed = new[] { new Thing { Type = "Country", Name = b.Country == "AL" ? "Albania" : b.Country } },
            ContactPoint = new[]
            {
                new ContactPoint
                {
                    ContactType = "customer support",
                    Email = b.Email,
                    Telephone = string.IsNullOrWhiteSpace(b.Phone) ? null : b.Phone,
                    AvailableLanguage = new[] { "sq", "en" },
                    HoursAvailable = b.SupportAllDay ? new[]
                    {
                        new OpeningHoursSpecification
                        {
                            DayOfWeek = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }.Select(d => $"https://schema.org/{d}").ToList(),
                            Opens = "00:00",
                            Closes = "23:59"
                        }
                    } : null
                }
            },
            SameAs = NonEmpty(b.SameAs),
            ParentOrganization = new Organization
            {
                Name = b.ParentName,
                Url = b.ParentUrl,
                SameAs = parentSameAs.Count == 0 ? null : parentSameAs
            }
        };
    }

    public static WebSite WebSite(MarketingFacts f) => new()
    {
        Id = WebSiteId(f.BaseUrl),
        Name = f.Brand.Name,
        Url = f.BaseUrl + "/",
        Description = f.Definition,
        InLanguage = new[] { "en", "sq" },
        Publisher = Thing.Ref(OrganizationId(f.BaseUrl))
    };

    /// <summary>The product, with one offer per plan shown on the page (prices from the price book).</summary>
    public static SoftwareApplication Software(MarketingFacts f, IReadOnlyList<string>? features = null) => new()
    {
        Id = SoftwareId(f.BaseUrl),
        Name = f.Brand.Name,
        Url = f.BaseUrl + "/",
        Description = f.Definition,
        ApplicationCategory = "BusinessApplication",
        OperatingSystem = "Web",
        Publisher = Thing.Ref(OrganizationId(f.BaseUrl)),
        FeatureList = features,
        Offers = f.Plans.Count == 0 ? null : f.Plans.Select(p => new Offer
        {
            Name = p.Name,
            Price = Money(p.MonthlyCents),
            PriceCurrency = f.Currency,
            Url = f.BaseUrl + "/pricing" + SeoLanguages.Query(f.Language, "en"),
            PriceSpecification = new UnitPriceSpecification
            {
                Price = Money(p.MonthlyCents),
                PriceCurrency = f.Currency,
                UnitText = "MONTH",
                ReferenceQuantity = new QuantitativeValue { Value = 1, UnitCode = "MON" }
            }
        }).ToList()
    };

    /// <summary>A marketing page: who we are, the site, the page (with breadcrumbs except on home), and extras.</summary>
    public static string ForMarketingPage(MarketingFacts f, string canonical, string title, IReadOnlyList<(string Name, string Url)>? breadcrumbs,
        bool withSoftware, IReadOnlyList<(string Question, string Answer)>? faq = null, string pageType = "WebPage", string? dateModified = null, IReadOnlyList<Thing>? extra = null)
    {
        var graph = new List<Thing> { Organization(f), WebSite(f) };
        if (withSoftware) graph.Add(Software(f));
        Thing? crumbRef = null;
        if (breadcrumbs is { Count: > 1 })
        {
            graph.Add(Breadcrumbs(canonical, breadcrumbs));
            crumbRef = Thing.Ref(canonical + "#breadcrumb");
        }
        graph.Add(new WebPage(pageType)
        {
            Id = canonical + "#webpage",
            Url = canonical,
            Name = title,
            InLanguage = f.Language,
            IsPartOf = Thing.Ref(WebSiteId(f.BaseUrl)),
            About = withSoftware ? Thing.Ref(SoftwareId(f.BaseUrl)) : Thing.Ref(OrganizationId(f.BaseUrl)),
            Breadcrumb = crumbRef,
            DateModified = dateModified
        });
        if (faq is { Count: > 0 })
        {
            graph.Add(new FaqPage
            {
                Id = canonical + "#faq",
                InLanguage = f.Language,
                MainEntity = faq.Select(q => new Question { Name = q.Question, AcceptedAnswer = new Answer { Text = q.Answer } }).ToList()
            });
        }
        if (extra != null) graph.AddRange(extra);
        return Ld.Serialize(graph);
    }

    public static BreadcrumbList Breadcrumbs(string canonical, IReadOnlyList<(string Name, string Url)> crumbs) => new()
    {
        Id = canonical + "#breadcrumb",
        ItemListElement = crumbs.Select((c, i) => new ListItem { Position = i + 1, Name = c.Name, Item = c.Url }).ToList()
    };

    // ------------------------------------------------------------------ restaurants

    /// <summary>
    /// Everything about a restaurant that its menu or website shows, with the URLs already worked
    /// out (they depend on hosts and plans): see MenuController / SiteController.
    /// </summary>
    /// <param name="RestaurantId">Stable across its pages and languages: its website's address if published, else its menu's.</param>
    /// <param name="RestaurantUrl">The restaurant's home on the web: its website if published, else its menu.</param>
    /// <param name="MenuUrl">The menu in this language (canonical).</param>
    /// <param name="Country">ISO country of the owner's account, or null.</param>
    public record RestaurantFacts(
        Branch Branch,
        string Language,
        string RestaurantId,
        string RestaurantUrl,
        string MenuUrl,
        string? BookingUrl,
        string? About,
        string? Slogan,
        IReadOnlyList<string> SameAs,
        string? Country,
        Func<string, string> Absolute);

    /// <summary>The restaurant id both its menu and its website use.</summary>
    public static string RestaurantId(string homeUrlWithoutLanguage) => homeUrlWithoutLanguage + "#restaurant";

    /// <summary>The restaurant itself (Restaurant, CafeOrCoffeeShop, BarOrPub…).</summary>
    public static FoodEstablishment Restaurant(RestaurantFacts f, object? hasMenu)
    {
        var b = f.Branch;
        var image = !string.IsNullOrWhiteSpace(b.Banner) ? b.Banner : !string.IsNullOrWhiteSpace(b.ShareImage) ? b.ShareImage : b.Logo;
        var cuisine = Cuisines.Name(b.Cuisine, f.Language);
        return new FoodEstablishment
        {
            Type = Cuisines.SchemaType(b.BusinessType),
            Id = f.RestaurantId,
            Name = b.Name,
            Url = f.RestaurantUrl,
            Description = string.IsNullOrWhiteSpace(f.About) ? null : f.About,
            Slogan = string.IsNullOrWhiteSpace(f.Slogan) ? null : f.Slogan,
            Image = string.IsNullOrWhiteSpace(image) ? null : f.Absolute(image),
            Logo = string.IsNullOrWhiteSpace(b.Logo) ? null : new ImageObject { Url = f.Absolute(b.Logo) },
            Telephone = string.IsNullOrWhiteSpace(b.PhoneNumber) ? null : b.PhoneNumber.Trim(),
            Address = string.IsNullOrWhiteSpace(b.Address) && string.IsNullOrWhiteSpace(b.City) ? null : new PostalAddress
            {
                StreetAddress = string.IsNullOrWhiteSpace(b.Address) ? null : b.Address.Trim(),
                AddressLocality = string.IsNullOrWhiteSpace(b.City) ? null : b.City.Trim(),
                AddressCountry = f.Country
            },
            Geo = b.Latitude is { } lat && b.Longitude is { } lng ? new GeoCoordinates { Latitude = lat, Longitude = lng } : null,
            ServesCuisine = cuisine == null ? null : new[] { cuisine },
            PriceRange = PriceRange(b),
            CurrenciesAccepted = b.Currency,
            OpeningHoursSpecification = Hours(b),
            AcceptsReservations = f.BookingUrl ?? (object)false,
            HasMenu = hasMenu,
            HasMap = string.IsNullOrWhiteSpace(b.GoogleBusinessUrl) ? null : b.GoogleBusinessUrl,
            SameAs = NonEmpty(f.SameAs),
            PotentialAction = f.BookingUrl == null ? null : new Thing[]
            {
                new ReserveAction
                {
                    Target = new EntryPoint
                    {
                        UrlTemplate = f.BookingUrl,
                        ActionPlatform = new[] { "https://schema.org/DesktopWebPlatform", "https://schema.org/MobileWebPlatform" }
                    }
                }
            }
        };
    }

    /// <summary>
    /// A menu page: the restaurant, its whole menu in this language (sections, dishes, prices, diets),
    /// the page and its breadcrumbs. Placeholder sections and dishes ("Test") and dishes without a
    /// price are left out of the structured data (the guest menu still shows them).
    /// </summary>
    public static string ForBranchMenu(RestaurantFacts f, string title, IReadOnlyList<(string Name, string Url)> breadcrumbs)
    {
        var b = f.Branch;
        var menuId = f.MenuUrl + "#menu";
        var sections = new List<MenuSection>();
        foreach (var category in (b.Categories ?? new List<Category>()).OrderBy(c => c.Priority))
        {
            var categoryName = TranslationHelper.GetTranslation(category.Name, category.NameTranslations, f.Language);
            if (SeoText.IsPlaceholder(categoryName)) continue;
            var items = new List<MenuItem>();
            foreach (var p in (category.Products ?? new List<Product>()).OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id))
            {
                var name = TranslationHelper.GetTranslation(p.Name, p.NameTranslations, f.Language);
                if (SeoText.IsPlaceholder(name)) continue;
                var description = TranslationHelper.GetTranslation(p.Description ?? "", p.DescriptionTranslations, f.Language);
                var nutrition = TranslationHelper.GetTranslation(p.Nutritions ?? "", p.NutritionsTranslations, f.Language);
                items.Add(new MenuItem
                {
                    Name = SeoText.OneLine(name),
                    Description = string.IsNullOrWhiteSpace(description) ? null : SeoText.OneLine(description),
                    Image = string.IsNullOrWhiteSpace(p.Image) ? null : f.Absolute(p.Image),
                    Offers = p.Price <= 0 ? null : new Offer
                    {
                        Price = p.Price.ToString("0.00", CultureInfo.InvariantCulture),
                        PriceCurrency = b.Currency,
                        Availability = p.IsAvailable ? "https://schema.org/InStock" : "https://schema.org/SoldOut"
                    },
                    SuitableForDiet = Diets(p.Diets),
                    Nutrition = string.IsNullOrWhiteSpace(nutrition) ? null : new NutritionInformation { Description = SeoText.OneLine(nutrition) }
                });
            }
            if (items.Count == 0) continue;
            sections.Add(new MenuSection { Name = SeoText.OneLine(categoryName), HasMenuItem = items });
        }

        var menu = sections.Count == 0 ? null : new Menu
        {
            Id = menuId,
            Name = SeoText.MenuName(b.Name, f.Language),
            Url = f.MenuUrl,
            InLanguage = f.Language,
            HasMenuSection = sections
        };

        var graph = new List<Thing>
        {
            Restaurant(f, menu == null ? null : Thing.Ref(menuId))
        };
        if (menu != null) graph.Add(menu);
        graph.Add(Breadcrumbs(f.MenuUrl, breadcrumbs));
        graph.Add(new WebPage
        {
            Id = f.MenuUrl + "#webpage",
            Url = f.MenuUrl,
            Name = title,
            InLanguage = f.Language,
            About = Thing.Ref(f.RestaurantId),
            MainEntity = menu == null ? null : Thing.Ref(menuId),
            Breadcrumb = Thing.Ref(f.MenuUrl + "#breadcrumb")
        });
        return Ld.Serialize(graph);
    }

    /// <summary>A website page: the restaurant (same @id as on its menu), with its menu's URL and booking, and the page.</summary>
    public static string ForBranchSite(RestaurantFacts f, string pageUrl, string title)
    {
        var graph = new List<Thing>
        {
            Restaurant(f, f.MenuUrl),
            new WebPage
            {
                Id = pageUrl + "#webpage",
                Url = pageUrl,
                Name = title,
                InLanguage = f.Language,
                About = Thing.Ref(f.RestaurantId)
            }
        };
        return Ld.Serialize(graph);
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>
    /// Opening hours from the branch's real periods, one entry per set of days with the same times
    /// (Mon-Fri 08:00-23:00, Sat 10:00-02:00…). A period past midnight closes "earlier" than it
    /// opens, which Google reads correctly; open 24 hours is 00:00-23:59. Days without a period are
    /// closed and simply not listed. Null when the branch doesn't show hours.
    /// </summary>
    public static List<OpeningHoursSpecification>? Hours(Branch b)
    {
        if (!b.HoursEnabled || b.OpeningHours is not { Count: > 0 } hours) return null;
        static int MondayFirst(DayOfWeek d) => ((int)d + 6) % 7;
        return hours
            .Select(h => (h.DayOfWeek, Opens: h.Opens == h.Closes ? "00:00" : OpeningHours.Time(h.Opens), Closes: h.Opens == h.Closes ? "23:59" : OpeningHours.Time(h.Closes)))
            .GroupBy(h => (h.Opens, h.Closes))
            .Select(g => new
            {
                Days = g.Select(x => x.DayOfWeek).Distinct().OrderBy(MondayFirst).ToList(),
                g.Key.Opens,
                g.Key.Closes
            })
            .OrderBy(x => MondayFirst(x.Days[0])).ThenBy(x => x.Opens)
            .Select(x => new OpeningHoursSpecification
            {
                DayOfWeek = x.Days.Select(d => $"https://schema.org/{d}").ToList(),
                Opens = x.Opens,
                Closes = x.Closes
            })
            .ToList();
    }

    /// <summary>schema.org diets for the dish's tags (spicy has no equivalent).</summary>
    public static List<string>? Diets(Diet diets)
    {
        var list = new List<string>();
        if (diets.HasFlag(Diet.Vegan)) list.Add("https://schema.org/VeganDiet");
        if (diets.HasFlag(Diet.Vegetarian)) list.Add("https://schema.org/VegetarianDiet");
        if (diets.HasFlag(Diet.GlutenFree)) list.Add("https://schema.org/GlutenFreeDiet");
        if (diets.HasFlag(Diet.Halal)) list.Add("https://schema.org/HalalDiet");
        return list.Count == 0 ? null : list;
    }

    /// <summary>"€8–€24": the cheapest and dearest priced dish on the menu, as guests see them.</summary>
    public static string? PriceRange(Branch b)
    {
        var prices = (b.Categories ?? new List<Category>()).Where(c => !SeoText.IsPlaceholder(c.Name))
            .SelectMany(c => c.Products ?? new List<Product>())
            .Where(p => p.Price > 0 && !SeoText.IsPlaceholder(p.Name)).Select(p => p.Price).ToList();
        if (prices.Count == 0) return null;
        var symbol = CurrencyHelper.GetCurrencySymbol(b.Currency);
        string F(decimal v) => v.ToString(v % 1 == 0 ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);
        var prefix = symbol is "€" or "$" or "£";
        string Price(decimal v) => prefix ? symbol + F(v) : F(v) + " " + symbol;
        var (min, max) = (prices.Min(), prices.Max());
        if (min == max) return Price(min);
        return prefix ? $"{Price(min)}–{Price(max)}" : $"{F(min)}–{F(max)} {symbol}";
    }

    private static string Money(int cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string>? NonEmpty(IEnumerable<string>? values)
    {
        var list = (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        return list.Count == 0 ? null : list;
    }
}
