using RestaurantMenu.Interfaces;

namespace RestaurantMenu.Models;

public class Product : ISoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string? Nutritions { get; set; }
    public decimal Price { get; set; }
    public string? Image { get; set; }
    public int DisplayOrder { get; set; }

    // False while a dish is sold out. Flipped from the dish row on Branch Details during
    // service; the guest menu shows a "Sold out" chip, or hides it (Branch.HideSoldOut).
    public bool IsAvailable { get; set; } = true;

    // The 14 EU allergens as a bit mask. Null means the owner hasn't declared them yet,
    // which is not the same as None (declared: contains none of them). Guests filtering
    // by allergen never see undeclared dishes as safe.
    public Allergen? Allergens { get; set; }
    public Diet Diets { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }
        
    public int BranchId { get; set; }
    
    public string? NameTranslations { get; set; }
    public string? DescriptionTranslations { get; set; }
    public string? NutritionsTranslations { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
}