using System.Globalization;
using System.Text.Json;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Builds schema.org JSON-LD. Everything is serialised with the default
/// System.Text.Json encoder, which escapes &lt; &gt; and &amp; to \uXXXX — that is what
/// keeps restaurant-supplied text from breaking out of the surrounding
/// &lt;script&gt; element, so do not swap in UnsafeRelaxedJsonEscaping here.
/// </summary>
public static class StructuredData
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Organization + WebSite + SoftwareApplication for the marketing landing page.</summary>
    public static string ForLandingPage(SeoService seo, string description)
    {
        var baseUrl = seo.BaseUrl;

        var organization = new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["@id"] = $"{baseUrl}/#organization",
            ["name"] = seo.SiteName,
            ["url"] = seo.Url("/"),
            ["description"] = description,
            ["logo"] = new Dictionary<string, object?>
            {
                ["@type"] = "ImageObject",
                ["url"] = seo.Absolute("/logo.png"),
                ["width"] = 774,
                ["height"] = 774
            }
        };

        var website = new Dictionary<string, object?>
        {
            ["@type"] = "WebSite",
            ["@id"] = $"{baseUrl}/#website",
            ["url"] = seo.Url("/"),
            ["name"] = seo.SiteName,
            ["description"] = description,
            ["inLanguage"] = SeoService.DefaultLanguage,
            ["publisher"] = new Dictionary<string, object?> { ["@id"] = $"{baseUrl}/#organization" }
        };

        // Deliberately no "offers" node: the page advertises a free tier but also
        // paid plans, and asserting a price here that the site never states is the
        // kind of unsupported claim Google penalises.
        var application = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["@id"] = $"{baseUrl}/#application",
            ["name"] = seo.SiteName,
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web",
            ["url"] = seo.Url("/"),
            ["description"] = description,
            ["publisher"] = new Dictionary<string, object?> { ["@id"] = $"{baseUrl}/#organization" }
        };

        return Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new object[] { organization, website, application }
        });
    }

    /// <summary>
    /// Restaurant + its full Menu + a breadcrumb trail. This is the payload that can
    /// win rich results for a branch, so every dish, price and section is included.
    /// </summary>
    public static string ForBranchMenu(Branch branch, string language, SeoService seo)
    {
        var canonical = seo.MenuUrl(branch.Name, language);

        var sections = new List<object>();
        foreach (var category in (branch.Categories ?? new List<Category>()).OrderBy(c => c.Priority))
        {
            var items = new List<object>();
            foreach (var product in (category.Products ?? new List<Product>()).OrderBy(p => p.DisplayOrder))
            {
                var item = new Dictionary<string, object?>
                {
                    ["@type"] = "MenuItem",
                    ["name"] = TranslationHelper.GetTranslation(product.Name, product.NameTranslations, language)
                };

                var itemDescription = TranslationHelper.GetTranslation(
                    product.Description, product.DescriptionTranslations, language);
                if (!string.IsNullOrWhiteSpace(itemDescription))
                {
                    item["description"] = itemDescription;
                }

                if (!string.IsNullOrWhiteSpace(product.Image))
                {
                    item["image"] = seo.Absolute(product.Image);
                }

                var nutrition = TranslationHelper.GetTranslation(
                    product.Nutritions ?? string.Empty, product.NutritionsTranslations, language);
                if (!string.IsNullOrWhiteSpace(nutrition))
                {
                    item["nutrition"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "NutritionInformation",
                        ["description"] = nutrition
                    };
                }

                item["offers"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Offer",
                    ["price"] = product.Price.ToString("0.00", CultureInfo.InvariantCulture),
                    ["priceCurrency"] = branch.Currency,
                    ["availability"] = product.IsAvailable ? "https://schema.org/InStock" : "https://schema.org/SoldOut"
                };

                items.Add(item);
            }

            // A section with no dishes is noise to a crawler; skip it.
            if (items.Count == 0)
            {
                continue;
            }

            sections.Add(new Dictionary<string, object?>
            {
                ["@type"] = "MenuSection",
                ["name"] = TranslationHelper.GetTranslation(category.Name, category.NameTranslations, language),
                ["hasMenuItem"] = items
            });
        }

        var restaurant = new Dictionary<string, object?>
        {
            ["@type"] = "Restaurant",
            ["@id"] = $"{canonical}#restaurant",
            ["name"] = branch.Name,
            ["url"] = canonical,
            ["currenciesAccepted"] = branch.Currency
        };

        if (!string.IsNullOrWhiteSpace(branch.Logo))
        {
            restaurant["logo"] = seo.Absolute(branch.Logo);
        }

        // Banner first: it is the wide photo of the room, which is what an image-rich
        // result wants. Logo is the fallback.
        var image = !string.IsNullOrWhiteSpace(branch.Banner) ? branch.Banner : branch.Logo;
        if (!string.IsNullOrWhiteSpace(image))
        {
            restaurant["image"] = seo.Absolute(image);
        }

        if (!string.IsNullOrWhiteSpace(branch.PhoneNumber))
        {
            restaurant["telephone"] = branch.PhoneNumber;
        }

        if (!string.IsNullOrWhiteSpace(branch.Address))
        {
            restaurant["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = branch.Address
            };
        }

        if (sections.Count > 0)
        {
            restaurant["hasMenu"] = new Dictionary<string, object?>
            {
                ["@type"] = "Menu",
                ["@id"] = $"{canonical}#menu",
                ["name"] = $"{branch.Name} menu",
                ["inLanguage"] = language,
                ["hasMenuSection"] = sections
            };
        }

        var breadcrumbs = new Dictionary<string, object?>
        {
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem",
                    ["position"] = 1,
                    ["name"] = seo.SiteName,
                    ["item"] = seo.Url("/")
                },
                new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem",
                    ["position"] = 2,
                    ["name"] = branch.Name,
                    ["item"] = canonical
                }
            }
        };

        return Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new object[] { restaurant, breadcrumbs }
        });
    }
}
