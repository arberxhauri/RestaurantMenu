using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
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
    /// <summary>The bookings day view: add, confirm, seat, cancel, mark no-shows, close a day (Editor and up).</summary>
    Bookings,
    /// <summary>Stock levels: record deliveries, usage, waste and counts; see shifts (Editor and up).</summary>
    Stock,
    /// <summary>Delete and restore dishes (Manager and up).</summary>
    DeleteDishes,
    /// <summary>Create, edit, delete and reorder categories (Manager and up).</summary>
    EditCategories,
    /// <summary>Branch details, opening hours, brand, tables and ordering, booking settings, website, ingredients and recipes, shifts (Manager and up).</summary>
    EditBranch,
    /// <summary>Insights, sales and the end-of-day close (Manager and up).</summary>
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

    /// <summary>Whether the branch's plan includes a module right now (false for a paused branch or a read-only account).</summary>
    Task<bool> HasModuleAsync(int branchId, BillingModule module);

    /// <summary>Whether the branch can be changed: its owner's account isn't read-only and the branch isn't paused.</summary>
    Task<bool> CanWriteAsync(int branchId);
}

/// <summary>Keys for what AccountGateFilter and BranchAccess share about the current request.</summary>
public static class AccountGate
{
    /// <summary>HashSet&lt;int&gt;: branches the current user works on that can't be changed right now.</summary>
    public const string ReadOnlyBranches = "AccountGate.ReadOnlyBranches";
    /// <summary>string: why a change was just refused (shown instead of a 404).</summary>
    public const string Blocked = "AccountGate.Blocked";

    /// <summary>A script's request (fetch with JSON), which gets a 409 with { message } instead of a redirect.</summary>
    public static bool WantsJson(HttpRequest request) =>
        request.HasJsonContentType()
        || request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        || request.Headers.XRequestedWith == "XMLHttpRequest";

    public static bool IsWrite(HttpRequest? request) =>
        request != null && !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));

    /// <summary>
    /// The refusal for a read-only account. Short on purpose: the banner above every page
    /// (_PlanNotice) already says why and how to continue.
    /// </summary>
    public const string ReadOnlyRefusal = "Not saved: the back office is read-only until the plan is renewed.";

    /// <summary>The message for a branch that can't be changed.</summary>
    public static string Reason(BranchEntitlements e, string? branchName = null) =>
        e.Paused && e.Account.CanWrite
            ? $"Not saved: {branchName ?? "this branch"} is paused, because your plan includes {e.Account.MaxBranches} branch{(e.Account.MaxBranches == 1 ? "" : "es")}. Choose which stay active on My branches. Its menu stays online."
            : ReadOnlyRefusal;
}

public class BranchAccess : IBranchAccess
{
    private readonly ApplicationDbContext _db;
    private readonly string? _userId;
    private readonly Dictionary<int, BranchRole> _cache = new();
    private readonly IEntitlementService _entitlements;
    private readonly HttpContext? _http;

    public BranchAccess(ApplicationDbContext db, IHttpContextAccessor http, UserManager<ApplicationUser> users, IEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
        _http = http.HttpContext;
        var principal = http.HttpContext?.User;
        _userId = principal?.Identity?.IsAuthenticated == true ? users.GetUserId(principal) : null;
    }

    /// <summary>The lowest role that has a permission. The single permission table.</summary>
    public static BranchRole Required(BranchPermission permission) => permission switch
    {
        BranchPermission.View or BranchPermission.EditDishes or BranchPermission.Kitchen or BranchPermission.Bookings or BranchPermission.Stock => BranchRole.Editor,
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

    /// <summary>
    /// The role check, and for changes (any request but GET/HEAD) also the plan: a read-only
    /// account or a paused branch can be looked at, not changed. A refusal for that reason is
    /// noted for AccountGateFilter, which explains it instead of showing "not found".
    /// </summary>
    public async Task<bool> CanAsync(int branchId, BranchPermission permission)
    {
        if (!Allows(await RoleAsync(branchId), permission)) return false;
        if (permission == BranchPermission.View || !AccountGate.IsWrite(_http?.Request)) return true;
        var e = await _entitlements.ForBranchAsync(branchId);
        if (e == null || e.CanWrite) return true;
        if (_http != null) _http.Items[AccountGate.Blocked] = AccountGate.Reason(e);
        return false;
    }

    public IQueryable<Branch> Branches(BranchPermission permission)
    {
        if (_userId == null) return _db.Branches.Where(_ => false);
        var userId = _userId;
        var min = Required(permission);
        IQueryable<Branch> query;
        if (min == BranchRole.Owner)
        {
            query = _db.Branches.Where(b => b.UserId == userId);
        }
        else
        {
            var minMember = min == BranchRole.Manager ? BranchMemberRole.Manager : BranchMemberRole.Editor;
            query = _db.Branches.Where(b => b.UserId == userId || b.Members!.Any(m => m.UserId == userId && m.Role >= minMember));
        }

        // Changes leave out branches that can't be changed right now (worked out once per
        // request by AccountGateFilter, which also explains the refusal).
        if (permission != BranchPermission.View && AccountGate.IsWrite(_http?.Request)
            && _http?.Items[AccountGate.ReadOnlyBranches] is HashSet<int> { Count: > 0 } readOnly)
        {
            var ids = readOnly.ToList();
            query = query.Where(b => !ids.Contains(b.Id));
        }
        return query;
    }

    public async Task<bool> HasModuleAsync(int branchId, BillingModule module) =>
        (await _entitlements.ForBranchAsync(branchId))?.Has(module) == true;

    public async Task<bool> CanWriteAsync(int branchId) =>
        (await _entitlements.ForBranchAsync(branchId))?.CanWrite == true;

    public IQueryable<int> BranchIds(BranchPermission permission) => Branches(permission).Select(b => b.Id);
}
