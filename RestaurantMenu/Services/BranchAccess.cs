using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>What the current user is to a branch. Higher includes everything below it.</summary>
public enum BranchRole
{
    None = 0,
    Editor = 1,
    Manager = 2,
    Owner = 3
}

/// <summary>Everything the back office lets someone do to a branch.</summary>
public enum BranchPermission
{
    /// <summary>Open the branch page and see its menu (Editor and up).</summary>
    View,
    /// <summary>Add and edit dishes, sold out, recommend, reorder dishes, photos, translate (Editor and up).</summary>
    EditDishes,
    /// <summary>The kitchen display: see table orders, move them along, pause new orders (Editor and up).</summary>
    Kitchen,
    /// <summary>Delete and restore dishes (Manager and up).</summary>
    DeleteDishes,
    /// <summary>Create, edit, delete and reorder categories (Manager and up).</summary>
    EditCategories,
    /// <summary>Branch details, opening hours, brand, tables and ordering (Manager and up).</summary>
    EditBranch,
    /// <summary>Insights (Manager and up).</summary>
    ViewInsights,
    /// <summary>QR codes and printing (Manager and up).</summary>
    Print,
    /// <summary>Invite, change and remove staff (Owner only).</summary>
    ManageTeam,
    /// <summary>Delete or restore the branch itself (Owner only).</summary>
    DeleteBranch
}

/// <summary>
/// The one place that decides who may do what to a branch: its owner (Branch.UserId), or
/// a BranchMember with a high enough role. Every back-office action goes through here,
/// either by checking a branch id or by composing <see cref="BranchIds"/> into its query,
/// so the rule can't drift between controllers. Deleted branches and removed users never
/// pass (global query filters).
/// </summary>
public interface IBranchAccess
{
    /// <summary>The current user's role on a branch; None if it doesn't exist or isn't theirs.</summary>
    Task<BranchRole> RoleAsync(int branchId);

    Task<bool> CanAsync(int branchId, BranchPermission permission);

    /// <summary>Branches the current user may act on with this permission (compose into queries).</summary>
    IQueryable<Branch> Branches(BranchPermission permission);

    /// <summary>Ids of those branches, for "entity.BranchId is allowed" in a single query.</summary>
    IQueryable<int> BranchIds(BranchPermission permission);
}

public class BranchAccess : IBranchAccess
{
    private readonly ApplicationDbContext _db;
    private readonly string? _userId;
    private readonly Dictionary<int, BranchRole> _cache = new();

    public BranchAccess(ApplicationDbContext db, IHttpContextAccessor http, UserManager<ApplicationUser> users)
    {
        _db = db;
        var principal = http.HttpContext?.User;
        _userId = principal?.Identity?.IsAuthenticated == true ? users.GetUserId(principal) : null;
    }

    /// <summary>The lowest role that has a permission. The single permission table.</summary>
    public static BranchRole Required(BranchPermission permission) => permission switch
    {
        BranchPermission.View or BranchPermission.EditDishes or BranchPermission.Kitchen => BranchRole.Editor,
        BranchPermission.DeleteDishes or BranchPermission.EditCategories or BranchPermission.EditBranch
            or BranchPermission.ViewInsights or BranchPermission.Print => BranchRole.Manager,
        _ => BranchRole.Owner
    };

    public static bool Allows(BranchRole role, BranchPermission permission) => role >= Required(permission);

    public async Task<BranchRole> RoleAsync(int branchId)
    {
        if (_userId == null) return BranchRole.None;
        if (_cache.TryGetValue(branchId, out var cached)) return cached;
        var role = await RoleOfAsync(_db, _userId, branchId);
        _cache[branchId] = role;
        return role;
    }

    /// <summary>A given user's role on a branch, for places without the current request (the kitchen hub).</summary>
    public static async Task<BranchRole> RoleOfAsync(ApplicationDbContext db, string userId, int branchId)
    {
        var info = await db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => new
            {
                b.UserId,
                Member = b.Members!.Where(m => m.UserId == userId).Select(m => (BranchMemberRole?)m.Role).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return info == null ? BranchRole.None
            : info.UserId == userId ? BranchRole.Owner
            : info.Member == BranchMemberRole.Manager ? BranchRole.Manager
            : info.Member == BranchMemberRole.Editor ? BranchRole.Editor
            : BranchRole.None;
    }

    public async Task<bool> CanAsync(int branchId, BranchPermission permission) =>
        Allows(await RoleAsync(branchId), permission);

    public IQueryable<Branch> Branches(BranchPermission permission)
    {
        if (_userId == null) return _db.Branches.Where(_ => false);
        var userId = _userId;
        var min = Required(permission);
        if (min == BranchRole.Owner)
        {
            return _db.Branches.Where(b => b.UserId == userId);
        }
        var minMember = min == BranchRole.Manager ? BranchMemberRole.Manager : BranchMemberRole.Editor;
        return _db.Branches.Where(b => b.UserId == userId || b.Members!.Any(m => m.UserId == userId && m.Role >= minMember));
    }

    public IQueryable<int> BranchIds(BranchPermission permission) => Branches(permission).Select(b => b.Id);
}
