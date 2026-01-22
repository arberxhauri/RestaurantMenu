namespace RestaurantMenu.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Priority { get; set; }
        
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
        
    public ICollection<Product>? Products { get; set; }
    
    public string? NameTranslations { get; set; }
}