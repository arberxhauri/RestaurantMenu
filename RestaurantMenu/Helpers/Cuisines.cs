using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// The kinds of food a restaurant can say it serves (Search &amp; sharing page), as fixed keys so
/// titles, the directory and schema.org servesCuisine read naturally in every menu language.
/// Name: the cuisine on its own ("Italian"). Phrase: what the place is ("Italian restaurant").
/// Languages the table doesn't have fall back to English.
/// </summary>
public static class Cuisines
{
    public record Cuisine(string Key, IReadOnlyDictionary<string, string> Name, IReadOnlyDictionary<string, string> Phrase);

    private static Cuisine C(string key, string[] name, string[] phrase) =>
        new(key, Zip(name), Zip(phrase));

    // Order of the arrays: en, sq, it, de, fr, es, tr.
    private static readonly string[] Langs = { "en", "sq", "it", "de", "fr", "es", "tr" };
    private static Dictionary<string, string> Zip(string[] v) => Langs.Select((l, i) => (l, v[i])).ToDictionary(x => x.l, x => x.Item2);

    public static readonly IReadOnlyList<Cuisine> All = new[]
    {
        C("albanian",
            new[] { "Albanian", "Shqiptare", "Albanese", "Albanisch", "Albanaise", "Albanesa", "Arnavut" },
            new[] { "Albanian restaurant", "Restorant me kuzhinë shqiptare", "Ristorante albanese", "Albanisches Restaurant", "Restaurant albanais", "Restaurante albanés", "Arnavut restoranı" }),
        C("italian",
            new[] { "Italian", "Italiane", "Italiana", "Italienisch", "Italienne", "Italiana", "İtalyan" },
            new[] { "Italian restaurant", "Restorant italian", "Ristorante italiano", "Italienisches Restaurant", "Restaurant italien", "Restaurante italiano", "İtalyan restoranı" }),
        C("pizza",
            new[] { "Pizza", "Pica", "Pizza", "Pizza", "Pizza", "Pizza", "Pizza" },
            new[] { "Pizzeria", "Piceri", "Pizzeria", "Pizzeria", "Pizzeria", "Pizzería", "Pizzacı" }),
        C("mediterranean",
            new[] { "Mediterranean", "Mesdhetare", "Mediterranea", "Mediterran", "Méditerranéenne", "Mediterránea", "Akdeniz" },
            new[] { "Mediterranean restaurant", "Restorant mesdhetar", "Ristorante mediterraneo", "Mediterranes Restaurant", "Restaurant méditerranéen", "Restaurante mediterráneo", "Akdeniz restoranı" }),
        C("seafood",
            new[] { "Seafood", "Peshk dhe fruta deti", "Pesce", "Fisch & Meeresfrüchte", "Poissons et fruits de mer", "Pescados y mariscos", "Deniz ürünleri" },
            new[] { "Seafood restaurant", "Restorant peshku", "Ristorante di pesce", "Fischrestaurant", "Restaurant de poissons", "Marisquería", "Balık restoranı" }),
        C("grill",
            new[] { "Grill", "Zgara", "Griglia", "Grill", "Grillades", "Parrilla", "Izgara" },
            new[] { "Grill restaurant", "Restorant me zgarë", "Braceria", "Grillrestaurant", "Grill", "Asador", "Izgara restoranı" }),
        C("burger",
            new[] { "Burgers", "Burgera", "Hamburger", "Burger", "Burgers", "Hamburguesas", "Burger" },
            new[] { "Burger restaurant", "Restorant me burgera", "Hamburgeria", "Burger-Restaurant", "Restaurant de burgers", "Hamburguesería", "Burgerci" }),
        C("asian",
            new[] { "Asian", "Aziatike", "Asiatica", "Asiatisch", "Asiatique", "Asiática", "Asya" },
            new[] { "Asian restaurant", "Restorant aziatik", "Ristorante asiatico", "Asiatisches Restaurant", "Restaurant asiatique", "Restaurante asiático", "Asya restoranı" }),
        C("sushi",
            new[] { "Sushi", "Sushi", "Sushi", "Sushi", "Sushi", "Sushi", "Suşi" },
            new[] { "Sushi restaurant", "Restorant sushi", "Ristorante sushi", "Sushi-Restaurant", "Restaurant de sushis", "Restaurante de sushi", "Suşi restoranı" }),
        C("turkish",
            new[] { "Turkish", "Turke", "Turca", "Türkisch", "Turque", "Turca", "Türk" },
            new[] { "Turkish restaurant", "Restorant turk", "Ristorante turco", "Türkisches Restaurant", "Restaurant turc", "Restaurante turco", "Türk restoranı" }),
        C("greek",
            new[] { "Greek", "Greke", "Greca", "Griechisch", "Grecque", "Griega", "Yunan" },
            new[] { "Greek restaurant", "Restorant grek", "Ristorante greco", "Griechisches Restaurant", "Restaurant grec", "Restaurante griego", "Yunan restoranı" }),
        C("vegetarian",
            new[] { "Vegetarian", "Vegjetariane", "Vegetariana", "Vegetarisch", "Végétarienne", "Vegetariana", "Vejetaryen" },
            new[] { "Vegetarian restaurant", "Restorant vegjetarian", "Ristorante vegetariano", "Vegetarisches Restaurant", "Restaurant végétarien", "Restaurante vegetariano", "Vejetaryen restoran" }),
        C("desserts",
            new[] { "Desserts", "Ëmbëlsira", "Dolci", "Desserts", "Desserts", "Postres", "Tatlılar" },
            new[] { "Dessert shop", "Ëmbëltore", "Pasticceria", "Konditorei", "Pâtisserie", "Pastelería", "Tatlıcı" }),
        C("international",
            new[] { "International", "Ndërkombëtare", "Internazionale", "International", "Internationale", "Internacional", "Uluslararası" },
            new[] { "International restaurant", "Restorant me kuzhinë ndërkombëtare", "Ristorante internazionale", "Internationales Restaurant", "Restaurant international", "Restaurante internacional", "Uluslararası restoran" })
    };

    public static Cuisine? Find(string? key) => key == null ? null : All.FirstOrDefault(c => c.Key == key);

    private static string Pick(IReadOnlyDictionary<string, string> d, string language) =>
        d.TryGetValue(language, out var v) ? v : d["en"];

    public static string? Name(string? key, string language) => Find(key) is { } c ? Pick(c.Name, language) : null;

    /// <summary>
    /// What the place is, in the page's language: "Italian restaurant", "Café", "Bar"... The cuisine
    /// only colours a plain restaurant; a café or bar is called that whatever it serves.
    /// </summary>
    public static string Phrase(BusinessType type, string? cuisine, string language)
    {
        if (type == BusinessType.Restaurant && Find(cuisine) is { } c) return Pick(c.Phrase, language);
        return Pick(TypeNames[type], language);
    }

    public static readonly IReadOnlyDictionary<BusinessType, IReadOnlyDictionary<string, string>> TypeNames =
        new Dictionary<BusinessType, IReadOnlyDictionary<string, string>>
        {
            [BusinessType.Restaurant] = Zip(new[] { "Restaurant", "Restorant", "Ristorante", "Restaurant", "Restaurant", "Restaurante", "Restoran" }),
            [BusinessType.Cafe] = Zip(new[] { "Café", "Kafene", "Caffè", "Café", "Café", "Cafetería", "Kafe" }),
            [BusinessType.Bar] = Zip(new[] { "Bar", "Bar", "Bar", "Bar", "Bar", "Bar", "Bar" }),
            [BusinessType.Bakery] = Zip(new[] { "Bakery", "Furrë buke", "Panetteria", "Bäckerei", "Boulangerie", "Panadería", "Fırın" }),
            [BusinessType.FastFood] = Zip(new[] { "Fast food", "Fast food", "Fast food", "Schnellrestaurant", "Restauration rapide", "Comida rápida", "Fast food" }),
            [BusinessType.IceCream] = Zip(new[] { "Ice cream shop", "Akullore", "Gelateria", "Eisdiele", "Glacier", "Heladería", "Dondurmacı" })
        };

    /// <summary>The schema.org type for a kind of place.</summary>
    public static string SchemaType(BusinessType type) => type switch
    {
        BusinessType.Cafe => "CafeOrCoffeeShop",
        BusinessType.Bar => "BarOrPub",
        BusinessType.Bakery => "Bakery",
        BusinessType.FastFood => "FastFoodRestaurant",
        BusinessType.IceCream => "IceCreamShop",
        _ => "Restaurant"
    };
}
