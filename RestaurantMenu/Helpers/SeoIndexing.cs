using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Which restaurant pages may appear in search results and the sitemaps. Everything else is
/// served normally (guests with the link or QR code still get it) but marked noindex, follow.
/// </summary>
public static class SeoIndexing
{
    /// <summary>The owner hasn't switched off "Show my menu in Google" and an admin hasn't marked it a demo.</summary>
    public static bool Branch(Branch b) => b.ShowInSearch && !b.IsDemo;

    /// <summary>A menu with at least one dish a guest can see. An empty menu is a thin page.</summary>
    public static bool Menu(Branch b, int visibleDishes) => Branch(b) && visibleDishes > 0;

    /// <summary>A published website.</summary>
    public static bool Site(Branch b, bool published) => Branch(b) && published;
}
