namespace RestaurantMenu.Services;

/// <summary>
/// Who My Quick Menu is, said the same way everywhere: the footer, /about, /contact, the
/// Organization schema and llms.txt. Search engines and AI assistants trust an entity whose
/// name, contact details and profiles agree across every page. Bound from "Brand" in
/// appsettings.json (env vars Brand__Email, Brand__SameAs__0, ...).
/// </summary>
public class BrandOptions
{
    public string Name { get; set; } = "My Quick Menu";
    public string Email { get; set; } = "info@myquickmenu.com";
    /// <summary>International format, e.g. +355 69 633 7906.</summary>
    public string? Phone { get; set; }
    /// <summary>WhatsApp number in international format without spaces or +, e.g. 355696337906.</summary>
    public string? WhatsApp { get; set; }
    /// <summary>Support answers 24 hours a day, 7 days a week (contact page, schema contactPoint).</summary>
    public bool SupportAllDay { get; set; }
    public string? StreetAddress { get; set; }
    public string Locality { get; set; } = "Tirana";
    public string? PostalCode { get; set; }
    /// <summary>ISO 3166 country code.</summary>
    public string Country { get; set; } = "AL";
    /// <summary>My Quick Menu's own social profiles (Instagram, Facebook, LinkedIn…): schema.org sameAs.</summary>
    public List<string> SameAs { get; set; } = new();

    /// <summary>The company behind it.</summary>
    public string ParentName { get; set; } = "4CS";
    public string ParentUrl { get; set; } = "https://4cs.al";
    public List<string> ParentSameAs { get; set; } = new();

    public string PhoneHref => "tel:" + new string((Phone ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
    public string? WhatsAppHref => string.IsNullOrWhiteSpace(WhatsApp) ? null : "https://wa.me/" + WhatsApp;
}
