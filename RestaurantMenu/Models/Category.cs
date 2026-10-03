using RestaurantMenu.Interfaces;

namespace RestaurantMenu.Models;

public class Category : ISoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Priority { get; set; }
        
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
        
    public ICollection<Product>? Products { get; set; }
    
    public string? NameTranslations { get; set; }

    // When the category is served (breakfast, lunch, happy hour), in the branch's time
    // zone. No days and no times = always. AvailableTo earlier than AvailableFrom runs past
    // midnight. Outside the window the menu greys it out, or hides it when HideWhenUnavailable.
    public AvailableDays AvailableDays { get; set; }
    public TimeOnly? AvailableFrom { get; set; }
    public TimeOnly? AvailableTo { get; set; }
    public bool HideWhenUnavailable { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
}

/// <summary>Days a category is served. None (0) means every day. Persisted as int; never renumber.</summary>
[Flags]
public enum AvailableDays
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
}
