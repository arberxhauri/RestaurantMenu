namespace RestaurantMenu.Models;

/// <summary>
/// One anonymous thing a guest did on a public menu. Deliberately holds no visitor
/// identifier: no IP address, user agent, cookie or device id, so nothing here is
/// personal data (see the privacy page). Old rows are deleted by AnalyticsRetentionService.
/// </summary>
public class MenuEvent
{
    public long Id { get; set; }
    public int BranchId { get; set; }

    /// <summary>The dish, for DishOpen and AddToList.</summary>
    public int? ProductId { get; set; }

    public MenuEventType Type { get; set; }

    /// <summary>Menu language: the one shown (View) or chosen (LanguageSwitch).</summary>
    public string? Lang { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>Persisted as smallint; never renumber.</summary>
public enum MenuEventType : short
{
    View = 1,
    DishOpen = 2,
    AddToList = 3,
    LanguageSwitch = 4
}
