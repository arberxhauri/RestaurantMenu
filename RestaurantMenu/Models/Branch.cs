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

    // Opening hours: shown on the menu ("Open until 23:00") and given to Google only when
    // HoursEnabled, so a branch that hasn't entered hours never looks permanently closed.
    public bool HoursEnabled { get; set; }
    public string TimeZone { get; set; } = "Europe/Tirane";
    public ICollection<BranchHours>? OpeningHours { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
        
    public string UserId { get; set; }
    public ApplicationUser? User { get; set; }
        
    public ICollection<Category>? Categories { get; set; }

    // Staff who help run this branch (Team on Branch Details).
    public ICollection<BranchMember>? Members { get; set; }

    // Table ordering: guests at a set-up table (Tables page) send their list to the
    // kitchen display. OrdersPaused is the kitchen's "not taking orders right now".
    public bool OrderingEnabled { get; set; }
    public bool OrdersPaused { get; set; }
    // Daily order numbers (#1, #2…), handed out by one atomic UPDATE so orders arriving
    // at the same moment never share a number (OrderService.NextNumberAsync).
    public int OrderCounter { get; set; }
    public DateOnly? OrderCounterDay { get; set; }

    // Guest feedback (menu footer). Low ratings stay private to the restaurant; the Google
    // review link is offered after 4-5 stars, or after every rating (FeedbackGoogleForAll),
    // which is what Google's review policy asks for.
    public bool FeedbackEnabled { get; set; } = true;
    public string? GoogleReviewUrl { get; set; }
    public bool FeedbackGoogleForAll { get; set; }
    public bool FeedbackEmailOwner { get; set; } = true;
    public ICollection<DiningTable>? Tables { get; set; }
}