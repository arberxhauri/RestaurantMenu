namespace RestaurantMenu.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string? Nutritions { get; set; }
    public decimal Price { get; set; }
    public string? Image { get; set; }
    public int DisplayOrder { get; set; }
        
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
        
    public int BranchId { get; set; }
    
    public string? NameTranslations { get; set; }
    public string? DescriptionTranslations { get; set; }
    public string? NutritionsTranslations { get; set; }
}