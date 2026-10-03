namespace RestaurantMenu.Models;

/// <summary>
/// A choice on a dish: "Size" (required, pick 1) or "Extras" (optional, up to 3).
/// MinSelect 0 = optional, 1 = required. MaxSelect 1 = a single choice (radio buttons),
/// more = several (checkboxes).
/// </summary>
public class ProductOptionGroup
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string Name { get; set; } = "";
    public string? NameTranslations { get; set; }
    public int MinSelect { get; set; }
    public int MaxSelect { get; set; } = 1;
    public int DisplayOrder { get; set; }

    public ICollection<ProductOption> Options { get; set; } = new List<ProductOption>();
}

/// <summary>One option in a group, e.g. "Large" (+1.50) or "Extra cheese" (+0.80).</summary>
public class ProductOption
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ProductOptionGroup? Group { get; set; }

    public string Name { get; set; } = "";
    public string? NameTranslations { get; set; }
    /// <summary>Added to the dish price; negative for a cheaper choice (e.g. half portion).</summary>
    public decimal PriceDelta { get; set; }
    public int DisplayOrder { get; set; }
}
