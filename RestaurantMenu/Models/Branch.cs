using RestaurantMenu.Interfaces;

namespace RestaurantMenu.Models;

public class Branch : ISoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Logo { get; set; }
    public string? Banner { get; set; }
    public string Address { get; set; }
    public string PhoneNumber { get; set; }
    
    public string Currency { get; set; } = "ALL";
    public string SupportedLanguages { get; set; } = "en";
    public string? ThemeColors { get; set; }

    // Leave sold-out dishes off the guest menu instead of showing them with a "Sold out" chip.
    public bool HideSoldOut { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
        
    public string UserId { get; set; }
    public ApplicationUser? User { get; set; }
        
    public ICollection<Category>? Categories { get; set; }
}