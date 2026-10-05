using RestaurantMenu.Interfaces;

namespace RestaurantMenu.Models;

public class Branch : ISoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; }

    // The link segment for /menu/{slug}, /book/{slug}, /site/{slug} and the subdomain, set from
    // the name (SlugRules) and unique across all branches, deleted ones included, so a closed
    // restaurant's printed QR code never opens someone else's menu. Old links live in
    // BranchSlugAlias. Filled for existing rows at startup (BranchSlugs.BackfillAsync).
    public string Slug { get; set; } = string.Empty;
    public ICollection<BranchSlugAlias>? SlugAliases { get; set; }
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

    // The owner's pick of branches to keep running when the account has more live branches
    // than its plan allows: picked ones first, then the oldest; the rest are paused (menu
    // online, back office read-only, no ordering or bookings). EntitlementRules.PausedBranches.
    public bool KeepActive { get; set; }
        
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

/// <summary>
/// A link a branch used to have: its slug before a rename, or the name-based link from before
/// slugs were stored ("oliver'sitalian"). Requests for it are sent to the current link with a
/// 301, so printed QR codes keep working. Never deleted, except when the branch takes the
/// same link back.
/// </summary>
public class BranchSlugAlias
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public DateTime CreatedUtc { get; set; }
}
