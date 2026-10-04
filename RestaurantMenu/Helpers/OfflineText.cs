namespace RestaurantMenu.Helpers;

/// <summary>The note on a menu shown without a connection (saved copy from the service worker).</summary>
public static class OfflineText
{
    // {0} = the time the copy was saved, e.g. 19:42.
    private static readonly Dictionary<string, string> Saved = new()
    {
        ["en"] = "No connection. This is the menu as saved at {0}; prices and dishes may have changed.",
        ["sq"] = "Pa lidhje. Kjo është menuja e ruajtur në orën {0}; çmimet dhe pjatat mund të kenë ndryshuar.",
        ["it"] = "Nessuna connessione. Questo è il menu salvato alle {0}; prezzi e piatti potrebbero essere cambiati.",
        ["de"] = "Keine Verbindung. Das ist die Karte von {0} Uhr; Preise und Gerichte können sich geändert haben.",
        ["fr"] = "Pas de connexion. Voici le menu enregistré à {0} ; les prix et les plats ont pu changer.",
        ["es"] = "Sin conexión. Este es el menú guardado a las {0}; los precios y platos pueden haber cambiado.",
        ["tr"] = "Bağlantı yok. Bu, {0} saatinde kaydedilen menü; fiyatlar ve yemekler değişmiş olabilir."
    };

    public static string For(string? language) => Saved.TryGetValue(language ?? "en", out var t) ? t : Saved["en"];
}
