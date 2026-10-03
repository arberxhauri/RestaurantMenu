using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>Guest-facing words for featured dishes and dish badges, in every menu language.</summary>
public static class Highlights
{
    public static readonly DishBadge[] Badges =
        { DishBadge.New, DishBadge.ChefsPick, DishBadge.Popular, DishBadge.Seasonal, DishBadge.TodaysSpecial };

    // Order: row title, then the badges in the order of Badges.
    private static readonly Dictionary<string, string[]> Words = new()
    {
        ["en"] = new[] { "Recommended", "New", "Chef's pick", "Popular", "Seasonal", "Today's special" },
        ["sq"] = new[] { "Të rekomanduara", "E re", "Zgjedhja e shefit", "Popullore", "Sezonale", "Speciale e ditës" },
        ["it"] = new[] { "Consigliati", "Novità", "Scelta dello chef", "Popolare", "Di stagione", "Piatto del giorno" },
        ["de"] = new[] { "Empfohlen", "Neu", "Tipp vom Chef", "Beliebt", "Saisonal", "Tagesgericht" },
        ["fr"] = new[] { "Recommandés", "Nouveau", "Choix du chef", "Populaire", "De saison", "Plat du jour" },
        ["es"] = new[] { "Recomendados", "Nuevo", "Elección del chef", "Popular", "De temporada", "Plato del día" },
        ["tr"] = new[] { "Önerilenler", "Yeni", "Şefin önerisi", "Popüler", "Mevsimlik", "Günün yemeği" }
    };

    private static string[] For(string? language) => Words.TryGetValue(language ?? "en", out var w) ? w : Words["en"];

    public static string Title(string? language) => For(language)[0];

    public static string BadgeName(DishBadge badge, string? language) =>
        badge == DishBadge.None ? "" : For(language)[Array.IndexOf(Badges, badge) + 1];
}
