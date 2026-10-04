using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace RestaurantMenu.Helpers;

/// <summary>Domain names for restaurant websites: cleaning, validation, and the site's guest words.</summary>
public static class SiteRules
{
    public const int MaxDomainsPerBranch = 2;
    private static readonly Regex Label = new("^(?!-)[a-z0-9-]{1,63}(?<!-)$", RegexOptions.Compiled);

    /// <summary>
    /// "https://WWW.Example.al/menu" → "www.example.al"; "çajtore.al" → "xn--ajtore-4ua.al".
    /// Null when it isn't a public domain name (an IP address, "localhost", one label, bad characters).
    /// </summary>
    public static string? NormalizeHost(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var h = input.Trim().ToLowerInvariant();
        h = Regex.Replace(h, "^[a-z]+://", "");
        var cut = h.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) h = h[..cut];
        if (h.Contains('@')) return null;
        var colon = h.LastIndexOf(':');
        if (colon >= 0) h = h[..colon];
        h = h.TrimEnd('.');
        if (h.Length == 0 || IPAddress.TryParse(h, out _)) return null;
        try { h = new IdnMapping().GetAscii(h); }
        catch (ArgumentException) { return null; }
        var labels = h.Split('.');
        if (h.Length > 253 || labels.Length < 2 || !labels.All(l => Label.IsMatch(l)) || labels[^1].All(char.IsAsciiDigit)) return null;
        if (h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".local") || h.EndsWith(".internal")) return null;
        return h;
    }

    /// <summary>For display: punycode back to letters (xn--ajtore-4ua.al → çajtore.al).</summary>
    public static string DisplayHost(string host)
    {
        try { return new IdnMapping().GetUnicode(host); }
        catch (ArgumentException) { return host; }
    }

    /// <summary>The root of a host as the owner would name it: www.example.al → example.al.</summary>
    public static string WithoutWww(string host) => host.StartsWith("www.") ? host[4..] : host;

    public static bool IsApex(string host) => host.Count(c => c == '.') == 1 || Regex.IsMatch(host, @"^[^.]+\.(com|net|org|co|gov|edu)\.[a-z]{2}$");

    /// <summary>Instagram handle from "@name", "name" or an instagram.com link; null if not one.</summary>
    public static string? CleanInstagram(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        var m = Regex.Match(s, @"instagram\.com/([A-Za-z0-9._]{1,30})", RegexOptions.IgnoreCase);
        if (m.Success) s = m.Groups[1].Value;
        s = s.TrimStart('@');
        return Regex.IsMatch(s, @"^[A-Za-z0-9._]{1,30}$") ? s : null;
    }

    /// <summary>A Facebook page link (https://facebook.com/...), or null.</summary>
    public static string? CleanFacebook(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (!s.Contains("://")) s = "https://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == "https"
               && (u.Host == "facebook.com" || u.Host.EndsWith(".facebook.com") || u.Host == "fb.com")
               && s.Length <= 200 ? u.ToString() : null;
    }

    // ---------------------------------------------------------------- guest words

    public record Words(string SeeMenu, string Book, string Call, string About, string Highlights, string FullMenu,
        string Hours, string Today, string ClosedDay, string FindUs, string Directions, string ShowMap, string MapNote, string Follow);

    private static readonly Dictionary<string, Words> All = new()
    {
        ["en"] = new("See the menu", "Book a table", "Call", "About us", "From the menu", "See the full menu",
            "Opening hours", "Today", "Closed", "Find us", "Get directions", "Show map", "The map is loaded from Google Maps.", "Follow us"),
        ["sq"] = new("Shiko menunë", "Rezervo një tavolinë", "Telefono", "Rreth nesh", "Nga menuja", "Shiko gjithë menunë",
            "Orari", "Sot", "Mbyllur", "Na gjeni", "Udhëzime", "Shfaq hartën", "Harta ngarkohet nga Google Maps.", "Na ndiqni"),
        ["it"] = new("Vedi il menu", "Prenota un tavolo", "Chiama", "Chi siamo", "Dal menu", "Vedi tutto il menu",
            "Orari", "Oggi", "Chiuso", "Dove siamo", "Indicazioni", "Mostra la mappa", "La mappa viene caricata da Google Maps.", "Seguici"),
        ["de"] = new("Zur Speisekarte", "Tisch reservieren", "Anrufen", "Über uns", "Aus der Karte", "Ganze Speisekarte",
            "Öffnungszeiten", "Heute", "Geschlossen", "So findest du uns", "Route planen", "Karte anzeigen", "Die Karte wird von Google Maps geladen.", "Folge uns"),
        ["fr"] = new("Voir le menu", "Réserver une table", "Appeler", "À propos", "Extrait du menu", "Voir tout le menu",
            "Horaires", "Aujourd'hui", "Fermé", "Nous trouver", "Itinéraire", "Afficher la carte", "La carte est chargée depuis Google Maps.", "Suivez-nous"),
        ["es"] = new("Ver el menú", "Reservar mesa", "Llamar", "Sobre nosotros", "De nuestro menú", "Ver todo el menú",
            "Horario", "Hoy", "Cerrado", "Dónde estamos", "Cómo llegar", "Mostrar mapa", "El mapa se carga desde Google Maps.", "Síguenos"),
        ["tr"] = new("Menüyü gör", "Masa ayırt", "Ara", "Hakkımızda", "Menüden", "Tüm menüyü gör",
            "Çalışma saatleri", "Bugün", "Kapalı", "Bizi bulun", "Yol tarifi", "Haritayı göster", "Harita Google Haritalar'dan yüklenir.", "Bizi takip edin")
    };

    public static Words For(string? language) => All.TryGetValue(language ?? "en", out var w) ? w : All["en"];
}
