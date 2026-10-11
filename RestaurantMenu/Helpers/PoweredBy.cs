namespace RestaurantMenu.Helpers;

/// <summary>
/// The "… by My Quick Menu" link at the foot of guest pages: descriptive text in the guest's
/// language, pointing at the product page that explains it (a followed link, so every restaurant
/// page also tells search engines what My Quick Menu does).
/// </summary>
public static class PoweredBy
{
    public enum Kind { Menu, Site, Booking }

    private static readonly Dictionary<Kind, Dictionary<string, string>> Text = new()
    {
        [Kind.Menu] = new()
        {
            ["en"] = "Digital menu by My Quick Menu", ["sq"] = "Menu dixhitale nga My Quick Menu", ["it"] = "Menu digitale di My Quick Menu",
            ["de"] = "Digitale Speisekarte von My Quick Menu", ["fr"] = "Menu digital par My Quick Menu", ["es"] = "Carta digital de My Quick Menu",
            ["tr"] = "My Quick Menu dijital menü"
        },
        [Kind.Site] = new()
        {
            ["en"] = "Restaurant website by My Quick Menu", ["sq"] = "Faqe interneti nga My Quick Menu", ["it"] = "Sito del ristorante di My Quick Menu",
            ["de"] = "Restaurant-Website von My Quick Menu", ["fr"] = "Site de restaurant par My Quick Menu", ["es"] = "Web de restaurante de My Quick Menu",
            ["tr"] = "My Quick Menu restoran web sitesi"
        },
        [Kind.Booking] = new()
        {
            ["en"] = "Online bookings by My Quick Menu", ["sq"] = "Rezervime online nga My Quick Menu", ["it"] = "Prenotazioni online di My Quick Menu",
            ["de"] = "Online-Reservierung von My Quick Menu", ["fr"] = "Réservations en ligne par My Quick Menu", ["es"] = "Reservas online de My Quick Menu",
            ["tr"] = "My Quick Menu online rezervasyon"
        }
    };

    private static readonly Dictionary<Kind, string> Paths = new()
    {
        [Kind.Menu] = "/digital-menu", [Kind.Site] = "/restaurant-website", [Kind.Booking] = "/table-bookings"
    };

    /// <param name="baseUrl">The app's own address (the link must work from a restaurant's own domain too).</param>
    public static (string Href, string Text) For(Kind kind, string language, string baseUrl) =>
        (baseUrl.TrimEnd('/') + Paths[kind] + (language == "sq" ? "?lang=sq" : ""),
         Text[kind].TryGetValue(language, out var t) ? t : Text[kind]["en"]);
}
