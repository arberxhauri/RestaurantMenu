using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// The one place that decides who may do what to a branch: its owner (Branch.UserId), or
/// a BranchMember whose role allows it (BranchPermissions), or a paired station device on its own branch. Every back-office action goes through here,
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
    private readonly StationDeviceIdentity? _device;
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
        // A shared station tablet: only when no person is signed in on the same browser.
        _device = _userId == null ? StationAuth.Read(principal) : null;
    }

    /// <summary>The permission table lives in <see cref="BranchPermissions"/>; kept here for the views.</summary>
    public static bool Allows(BranchRole role, BranchPermission permission) => BranchPermissions.Allows(role, permission);

    public async Task<BranchRole> RoleAsync(int branchId)
    {
        if (_userId == null && _device == null) return BranchRole.None;
        if (_cache.TryGetValue(branchId, out var cached)) return cached;
        var role = await RoleOfAsync(_db, _userId, _device, branchId);
        _cache[branchId] = role;
        return role;
    }

    /// <summary>
    /// A person's or station device's role on a branch, for places without the current request (the
    /// kitchen hub). A device acts only on its own branch, with its station's role; its cookie was
    /// already checked against the database when the request was signed in.
    /// </summary>
    public static async Task<BranchRole> RoleOfAsync(ApplicationDbContext db, string? userId, StationDeviceIdentity? device, int branchId)
    {
        if (userId == null)
        {
            return device != null && device.BranchId == branchId && await db.Branches.AnyAsync(b => b.Id == branchId)
                ? StationDeviceRules.RoleFor(device.Station)
                : BranchRole.None;
        }

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
            : BranchPermissions.RoleOf(info.Member);
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
        IQueryable<Branch> query;
        if (_userId is { } userId)
        {
            var memberRoles = BranchPermissions.MemberRolesWith(permission).ToList();
            query = memberRoles.Count == 0
                ? _db.Branches.Where(b => b.UserId == userId)
                : _db.Branches.Where(b => b.UserId == userId || b.Members!.Any(m => m.UserId == userId && memberRoles.Contains(m.Role)));
        }
        else if (_device is { } device && BranchPermissions.Allows(StationDeviceRules.RoleFor(device.Station), permission))
        {
            query = _db.Branches.Where(b => b.Id == device.BranchId);
        }
        else
        {
            return _db.Branches.Where(_ => false);
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
