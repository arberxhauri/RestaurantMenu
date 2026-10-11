using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace RestaurantMenu.Helpers.JsonLd;

// Typed schema.org nodes for JSON-LD (docs/SEO-GEO.md, "Structured data"). Built by
// StructuredData, serialised by Ld.Serialize, never by concatenating strings. Only what the page
// itself shows goes in (Google's structured data policy). Nulls are left out.

/// <summary>Any schema.org thing. A node with only an @id is a reference to a node elsewhere in the graph.</summary>
public class Thing
{
    [JsonPropertyName("@type"), JsonPropertyOrder(-3)] public string? Type { get; init; }
    [JsonPropertyName("@id"), JsonPropertyOrder(-2)] public string? Id { get; init; }
    [JsonPropertyOrder(-1)] public string? Name { get; init; }
    public string? Url { get; init; }
    public string? Description { get; init; }
    /// <summary>A URL string or an <see cref="ImageObject"/>.</summary>
    public object? Image { get; init; }
    public IReadOnlyList<string>? SameAs { get; init; }

    /// <summary>{"@id": id}: points at a node defined elsewhere in the graph.</summary>
    public static Thing Ref(string id) => new() { Id = id };
}

public sealed class ImageObject : Thing
{
    public ImageObject() => Type = "ImageObject";
    public int? Width { get; init; }
    public int? Height { get; init; }
}

public sealed class PostalAddress : Thing
{
    public PostalAddress() => Type = "PostalAddress";
    public string? StreetAddress { get; init; }
    public string? AddressLocality { get; init; }
    public string? PostalCode { get; init; }
    public string? AddressCountry { get; init; }
}

public sealed class GeoCoordinates : Thing
{
    public GeoCoordinates() => Type = "GeoCoordinates";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed class ContactPoint : Thing
{
    public ContactPoint() => Type = "ContactPoint";
    public string? ContactType { get; init; }
    public string? Email { get; init; }
    public string? Telephone { get; init; }
    public IReadOnlyList<string>? AvailableLanguage { get; init; }
}

public sealed class Organization : Thing
{
    public Organization() => Type = "Organization";
    public ImageObject? Logo { get; init; }
    public string? Email { get; init; }
    public string? Telephone { get; init; }
    public PostalAddress? Address { get; init; }
    public IReadOnlyList<Thing>? AreaServed { get; init; }
    public IReadOnlyList<ContactPoint>? ContactPoint { get; init; }
    public Thing? ParentOrganization { get; init; }
}

public sealed class WebSite : Thing
{
    public WebSite() => Type = "WebSite";
    public Thing? Publisher { get; init; }
    /// <summary>A language code or a list of them.</summary>
    public object? InLanguage { get; init; }
}

public sealed class WebPage : Thing
{
    public WebPage(string type = "WebPage") => Type = type;
    public Thing? IsPartOf { get; init; }
    public Thing? About { get; init; }
    public Thing? Breadcrumb { get; init; }
    public string? InLanguage { get; init; }
    public string? DateModified { get; init; }
    public Thing? MainEntity { get; init; }
}

public sealed class SoftwareApplication : Thing
{
    public SoftwareApplication() => Type = "SoftwareApplication";
    public string? ApplicationCategory { get; init; }
    public string? OperatingSystem { get; init; }
    public IReadOnlyList<Offer>? Offers { get; init; }
    public Thing? Publisher { get; init; }
    public IReadOnlyList<string>? FeatureList { get; init; }
}

public sealed class Offer : Thing
{
    public Offer() => Type = "Offer";
    /// <summary>A decimal as a string ("5.00"): no float rounding, no culture.</summary>
    public string? Price { get; init; }
    public string? PriceCurrency { get; init; }
    public string? Availability { get; init; }
    public UnitPriceSpecification? PriceSpecification { get; init; }
}

public sealed class UnitPriceSpecification : Thing
{
    public UnitPriceSpecification() => Type = "UnitPriceSpecification";
    public string? Price { get; init; }
    public string? PriceCurrency { get; init; }
    /// <summary>"MONTH" for a monthly subscription.</summary>
    public string? UnitText { get; init; }
    public QuantitativeValue? ReferenceQuantity { get; init; }
}

public sealed class QuantitativeValue : Thing
{
    public QuantitativeValue() => Type = "QuantitativeValue";
    public int Value { get; init; }
    /// <summary>UN/CEFACT code: "MON" = month.</summary>
    public string? UnitCode { get; init; }
}

public sealed class BreadcrumbList : Thing
{
    public BreadcrumbList() => Type = "BreadcrumbList";
    public IReadOnlyList<ListItem>? ItemListElement { get; init; }
}

public sealed class ListItem : Thing
{
    public ListItem() => Type = "ListItem";
    public int Position { get; init; }
    /// <summary>The URL of the crumb (a string, as Google's examples).</summary>
    public string? Item { get; init; }
}

public sealed class ItemList : Thing
{
    public ItemList() => Type = "ItemList";
    public IReadOnlyList<ListItem>? ItemListElement { get; init; }
    public int? NumberOfItems { get; init; }
}

public sealed class FaqPage : Thing
{
    public FaqPage() => Type = "FAQPage";
    public IReadOnlyList<Question>? MainEntity { get; init; }
    public string? InLanguage { get; init; }
}

public sealed class Question : Thing
{
    public Question() => Type = "Question";
    public Answer? AcceptedAnswer { get; init; }
}

public sealed class Answer : Thing
{
    public Answer() => Type = "Answer";
    public string? Text { get; init; }
}

/// <summary>Restaurant, CafeOrCoffeeShop, BarOrPub, Bakery…: <see cref="Thing.Type"/> says which.</summary>
public sealed class FoodEstablishment : Thing
{
    public ImageObject? Logo { get; init; }
    public PostalAddress? Address { get; init; }
    public GeoCoordinates? Geo { get; init; }
    public string? Telephone { get; init; }
    public IReadOnlyList<string>? ServesCuisine { get; init; }
    public string? PriceRange { get; init; }
    public string? CurrenciesAccepted { get; init; }
    public string? Slogan { get; init; }
    public IReadOnlyList<OpeningHoursSpecification>? OpeningHoursSpecification { get; init; }
    /// <summary>true/false, or the booking page's URL.</summary>
    public object? AcceptsReservations { get; init; }
    /// <summary>The Menu node, a reference to it, or the menu's URL.</summary>
    public object? HasMenu { get; init; }
    public string? HasMap { get; init; }
    public IReadOnlyList<Thing>? PotentialAction { get; init; }
}

public sealed class OpeningHoursSpecification : Thing
{
    public OpeningHoursSpecification() => Type = "OpeningHoursSpecification";
    /// <summary>One day or several (https://schema.org/Monday …).</summary>
    public IReadOnlyList<string>? DayOfWeek { get; init; }
    public string? Opens { get; init; }
    public string? Closes { get; init; }
}

public sealed class Menu : Thing
{
    public Menu() => Type = "Menu";
    public string? InLanguage { get; init; }
    public IReadOnlyList<MenuSection>? HasMenuSection { get; init; }
}

public sealed class MenuSection : Thing
{
    public MenuSection() => Type = "MenuSection";
    public IReadOnlyList<MenuItem>? HasMenuItem { get; init; }
}

public sealed class MenuItem : Thing
{
    public MenuItem() => Type = "MenuItem";
    public Offer? Offers { get; init; }
    public IReadOnlyList<string>? SuitableForDiet { get; init; }
    public NutritionInformation? Nutrition { get; init; }
}

public sealed class NutritionInformation : Thing
{
    public NutritionInformation() => Type = "NutritionInformation";
}

public sealed class ReserveAction : Thing
{
    public ReserveAction() => Type = "ReserveAction";
    public EntryPoint? Target { get; init; }
}

public sealed class EntryPoint : Thing
{
    public EntryPoint() => Type = "EntryPoint";
    public string? UrlTemplate { get; init; }
    public IReadOnlyList<string>? ActionPlatform { get; init; }
}

/// <summary>Serialisation: one graph per page, HTML-safe.</summary>
public static class Ld
{
    /// <summary>
    /// Every character is kept as itself (ë, €, ’) except the ones that could end the surrounding
    /// &lt;script&gt; element or an attribute: &lt; &gt; &amp; ' " and + become \uXXXX, which every JSON parser
    /// reads back as the original character. Names are never HTML-encoded (no &amp;#x27;).
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
        Converters = { new RuntimeTypeConverter() }
    };

    public static string Serialize(IEnumerable<Thing> graph) =>
        JsonSerializer.Serialize(new Graph(graph.ToList()), Options);

    private sealed record Graph(
        [property: JsonPropertyName("@graph")] IReadOnlyList<Thing> Nodes)
    {
        [JsonPropertyName("@context"), JsonPropertyOrder(-1)] public string Context => "https://schema.org";
    }

    /// <summary>Writes a Thing as its actual type, so a list of Things keeps each node's own properties.</summary>
    private sealed class RuntimeTypeConverter : JsonConverter<Thing>
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Thing);

        public override Thing Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Thing value, JsonSerializerOptions options)
        {
            var type = value.GetType();
            if (type == typeof(Thing))
            {
                // A plain Thing (a reference or a simple named thing): write its own properties.
                writer.WriteStartObject();
                if (value.Type != null) writer.WriteString("@type", value.Type);
                if (value.Id != null) writer.WriteString("@id", value.Id);
                if (value.Name != null) writer.WriteString("name", value.Name);
                if (value.Url != null) writer.WriteString("url", value.Url);
                if (value.Description != null) writer.WriteString("description", value.Description);
                if (value.Image != null) { writer.WritePropertyName("image"); JsonSerializer.Serialize(writer, value.Image, value.Image.GetType(), options); }
                if (value.SameAs != null) { writer.WritePropertyName("sameAs"); JsonSerializer.Serialize(writer, value.SameAs, options); }
                writer.WriteEndObject();
                return;
            }
            JsonSerializer.Serialize(writer, value, type, options);
        }
    }
}
