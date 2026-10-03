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
    public BranchMemberRole Role { get; set; } = BranchMemberRole.Editor;
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Persisted as int; never renumber. Higher includes everything below it.</summary>
public enum BranchMemberRole
{
    /// <summary>Waiters: dishes and availability.</summary>
    Editor = 1,
    /// <summary>Managers: the whole menu and branch, but not the team or deleting the branch.</summary>
    Manager = 2
}
