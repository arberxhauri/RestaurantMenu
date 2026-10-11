using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// The facts a restaurant's menu and website both give to search engines (StructuredData),
/// worked out the same way on both so the restaurant is one entity: its home on the web (its
/// published website, else its menu), its profiles, its country.
/// </summary>
public class RestaurantSeo
{
    private readonly ApplicationDbContext _db;
    private readonly SeoService _seo;
    private readonly SiteHosts _hosts;
    private readonly IEntitlementService _plans;

    public RestaurantSeo(ApplicationDbContext db, SeoService seo, SiteHosts hosts, IEntitlementService plans)
    {
        _db = db;
        _seo = seo;
        _hosts = hosts;
        _plans = plans;
    }

    /// <summary>The published website's address (own domain, free subdomain or /site/{slug}), without a language; null when there is none.</summary>
    public async Task<string?> WebsiteHomeAsync(Branch b, BranchSite? site)
    {
        if (site?.Enabled != true || (await _plans.ForBranchAsync(b.Id))?.Has(BillingModule.Website) != true) return null;
        return await _hosts.PublicHostAsync(b.Id, b.Slug) is { } host ? $"https://{host}/" : _seo.Url($"/site/{b.Slug}");
    }

    /// <param name="bookingUrl">The booking page (absolute) when the restaurant takes bookings online.</param>
    public async Task<StructuredData.RestaurantFacts> FactsAsync(Branch b, string language, BranchSite? site, string? bookingUrl)
    {
        var home = await WebsiteHomeAsync(b, site);
        var restaurantUrl = home == null ? _seo.MenuUrl(b) : home;
        var sameAs = new List<string>();
        if (site != null && home != null)
        {
            if (!string.IsNullOrWhiteSpace(site.Instagram)) sameAs.Add($"https://www.instagram.com/{site.Instagram}/");
            if (!string.IsNullOrWhiteSpace(site.Facebook)) sameAs.Add(site.Facebook);
        }
        var country = await _db.Users.Where(u => u.Id == b.UserId).Select(u => u.Country).FirstOrDefaultAsync();
        var about = site == null || home == null ? null : TranslationHelper.GetTranslation(site.About ?? "", site.AboutTranslations, language);
        var tagline = site == null || home == null ? null : TranslationHelper.GetTranslation(site.Tagline ?? "", site.TaglineTranslations, language);
        return new StructuredData.RestaurantFacts(
            b, language,
            RestaurantId: StructuredData.RestaurantId(restaurantUrl),
            RestaurantUrl: restaurantUrl,
            MenuUrl: _seo.MenuUrl(b, language),
            BookingUrl: bookingUrl,
            About: string.IsNullOrWhiteSpace(about) ? null : SeoText.OneLine(about),
            Slogan: string.IsNullOrWhiteSpace(tagline) ? null : SeoText.OneLine(tagline),
            SameAs: sameAs,
            Country: string.IsNullOrWhiteSpace(country) || country == "ZZ" ? null : country,
            Absolute: _seo.Absolute);
    }
}
