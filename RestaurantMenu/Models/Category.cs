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

    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
}