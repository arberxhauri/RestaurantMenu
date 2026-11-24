namespace RestaurantMenu.Models;

/// <summary>
/// A person who helps run a branch without the owner's login. The owner is not a member;
/// ownership stays Branch.UserId. Access rules live in one place: Services/BranchAccess.
/// </summary>
public class BranchMember
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public BranchMemberRole Role { get; set; } = BranchMemberRole.Waiter;
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// Persisted as int; never renumber. Not a ladder: what each role may do is the table in
/// Services/BranchPermissions (docs/PLAN-backoffice.md §5).
/// </summary>
public enum BranchMemberRole
{
    /// <summary>Waiters (called Editor before phase 8): dishes and availability, POS, stations, floor, bookings, stock.</summary>
    Waiter = 1,
    /// <summary>Managers: the whole branch, but not the team or deleting the branch.</summary>
    Manager = 2,
    /// <summary>Cashiers: POS, payments, bills and the day's cash report.</summary>
    Cashier = 3,
    /// <summary>Kitchen staff: the kitchen screen only.</summary>
    Kitchen = 4,
    /// <summary>Bar staff: the bar screen only.</summary>
    Bar = 5,
    /// <summary>Stock keepers: stock items, deliveries, counts and waste.</summary>
    Stock = 6
}
