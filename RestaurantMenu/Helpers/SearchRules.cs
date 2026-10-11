using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>Input rules for the Search &amp; sharing page. Pure; tested in SearchRulesTests.</summary>
public static class SearchRules
{
    /// <summary>Database limits; the page's counters suggest 60 and 155, Google's display widths.</summary>
    public const int TitleMax = 70;
    public const int DescriptionMax = 200;

    /// <summary>
    /// Latitude and longitude as typed (or pasted from Google Maps as "41.3275, 19.8187" into the
    /// first box). Both empty = none. Returns an error in words when they don't make a place.
    /// </summary>
    public static (double? Lat, double? Lng, string? Error) Coordinates(string? latitude, string? longitude)
    {
        latitude = latitude?.Trim();
        longitude = longitude?.Trim();
        if (string.IsNullOrEmpty(longitude) && latitude?.Contains(',') == true)
        {
            var parts = latitude.Split(',', 2, StringSplitOptions.TrimEntries);
            (latitude, longitude) = (parts[0], parts[1]);
        }
        if (string.IsNullOrEmpty(latitude) && string.IsNullOrEmpty(longitude)) return (null, null, null);
        if (!double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
            || lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return (null, null, "The map location should be two numbers like 41.3275, 19.8187 (in Google Maps: right-click your restaurant and click the numbers to copy them).");
        }
        return (Math.Round(lat, 6), Math.Round(lng, 6), null);
    }

    private static readonly string[] GoogleHosts =
    {
        "google.com", "google.al", "maps.app.goo.gl", "goo.gl", "g.page", "g.co", "business.google.com", "maps.google.com"
    };

    /// <summary>A Google Maps / Business Profile link (https, a Google host), or null.</summary>
    public static string? GoogleBusinessUrl(string? input)
    {
        var s = input?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || s.Length > 500) return null;
        var host = uri.Host.ToLowerInvariant();
        var google = GoogleHosts.Any(h => host == h || host.EndsWith("." + h))
                     || host.StartsWith("www.google.") || host.StartsWith("maps.google.");
        return google ? new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.ToString() : null;
    }
}

/// <summary>
/// "Share &amp; get found": what a restaurant can still fill in so Google, Maps and AI assistants
/// describe it well (complete data = complete structured data). Shown on Search &amp; sharing with a
/// score, and as a card on the branch page.
/// </summary>
public record ProfileChecklist(IReadOnlyList<ProfileChecklist.Item> Items)
{
    /// <param name="Scored">False for tips the app can't check (e.g. the Instagram bio).</param>
    public record Item(string Key, bool Done, string Title, string Why, string? Action, bool Scored = true);

    public int Score
    {
        get
        {
            var scored = Items.Where(i => i.Scored).ToList();
            return scored.Count == 0 ? 100 : (int)Math.Round(100.0 * scored.Count(i => i.Done) / scored.Count);
        }
    }

    public static ProfileChecklist For(Branch b, int dishes, int dishPhotos, BranchSite? site, bool hasOwnDomain, bool siteOn)
    {
        var edit = $"/branch/edit/{b.Id}";
        var search = $"/branch/{b.Id}/search";
        var website = $"/branch/{b.Id}/website";
        var items = new List<Item>
        {
            new("visible", b.ShowInSearch && !b.IsDemo, "Your menu can be found on Google",
                "Switch on \"Show my menu in Google\" below.", null),
            new("dishes", dishes > 0, "Your menu has dishes", "An empty menu isn't shown in search results.", $"/branch/details/{b.Id}"),
            new("address", !string.IsNullOrWhiteSpace(b.Address), "Address", "Google Maps and assistants answer \"where is it?\" from it.", edit),
            new("city", !string.IsNullOrWhiteSpace(b.City), "City", "Puts your city in your search title, e.g. \"Menu & Prices · Tirana\".", search),
            new("phone", !string.IsNullOrWhiteSpace(b.PhoneNumber), "Phone number", "Lets guests call straight from search results.", edit),
            new("kind", b.BusinessType != BusinessType.Restaurant || b.Cuisine != null, "Type of place and cuisine",
                "Helps you show up for searches like \"Italian restaurant in Tirana\".", search),
            new("hours", b.HoursEnabled && b.OpeningHours?.Count > 0, "Opening hours", "Google shows \"Open now\" and assistants can say when you're open.", edit),
            new("logo", !string.IsNullOrWhiteSpace(b.Logo), "Logo", "Shown next to your name in previews and on your menu.", edit),
            new("banner", !string.IsNullOrWhiteSpace(b.Banner) || !string.IsNullOrWhiteSpace(b.ShareImage), "Banner photo",
                "The big picture in link previews on WhatsApp, Facebook and Google.", edit),
            new("photos", dishes > 0 && dishPhotos * 2 >= dishes, "Photos on at least half of your dishes",
                "Dish photos can appear in Google Images and make your menu stand out.", $"/branch/details/{b.Id}"),
            new("map", b.Latitude != null && b.Longitude != null, "Map location", "Pins your restaurant exactly, so directions are right.", search),
            new("google", !string.IsNullOrWhiteSpace(b.GoogleBusinessUrl), "Google Business Profile link",
                "Connects your menu with your listing on Google Maps. Create or claim it at business.google.com.", search),
            new("website", siteOn, "Website published", "A website on your own address ranks for your restaurant's name.", website)
        };
        if (siteOn)
        {
            items.Add(new("social", !string.IsNullOrWhiteSpace(site?.Instagram) || !string.IsNullOrWhiteSpace(site?.Facebook),
                "Instagram or Facebook on your website", "Links your profiles to your restaurant for Google.", website));
            items.Add(new("domain", hasOwnDomain, "Your own domain (optional)", "e.g. www.yourrestaurant.al: easier to remember and share.", website, Scored: false));
        }
        items.Add(new("bio", false, "Put your menu link in your Instagram bio",
            "Guests and search engines find your menu from your profile. We can't check this one for you.", null, Scored: false));
        return new ProfileChecklist(items);
    }
}
