using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// The seed for the plan settings and prices on the very first start, bound from "Billing"
/// (env vars Billing__SeatsPerBranch, Billing__Prices__Menu__Month, ...). After that the database
/// is the source (PlanSettingsService, Admin → Plans &amp; prices) and these are not read.
/// </summary>
public class BillingOptions
{
    /// <summary>Staff accounts included per branch on plans that aren't legacy.</summary>
    public int SeatsPerBranch { get; set; } = 5;
    /// <summary>Days a past-due account keeps working before it becomes read-only.</summary>
    public int GraceDays { get; set; } = 14;
    /// <summary>Length of a trial the admin starts without picking a date.</summary>
    public int TrialDays { get; set; } = 14;
    public string Currency { get; set; } = "EUR";
    /// <summary>
    /// Prices in cents, by module and interval: Billing__Prices__Ordering__Month=900. Copied into
    /// the PriceBook table at startup when they change (BillingSetup); unset modules have no price.
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> Prices { get; set; } = new();
}

/// <summary>
/// The one place that answers "what may this account, or this branch, do right now". Every gate
/// asks it (through IBranchAccess in the back office); nothing else reads Subscription or
/// ApplicationUser.NumberOfBranches. Scoped: one load per owner per request, nothing cached
/// across requests, so an admin's change applies on the next click.
/// </summary>
public interface IEntitlementService
{
    Task<Entitlements> ForOwnerAsync(string ownerId);

    /// <summary>A live branch's entitlements (its owner's, unless the branch is paused); null if it doesn't exist.</summary>
    Task<BranchEntitlements?> ForBranchAsync(int branchId);

    /// <summary>The owner's live branches that are paused for being over the plan's branch count.</summary>
    Task<IReadOnlySet<int>> PausedBranchesAsync(string ownerId);
}

public class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _db;
    private readonly PlanSettingsService _settings;
    private readonly Dictionary<string, Entitlements> _owners = new();
    private readonly Dictionary<string, IReadOnlySet<int>> _paused = new();
    private readonly Dictionary<int, BranchEntitlements?> _branches = new();

    public EntitlementService(ApplicationDbContext db, PlanSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<Entitlements> ForOwnerAsync(string ownerId)
    {
        if (_owners.TryGetValue(ownerId, out var cached)) return cached;
        var snapshot = await SnapshotAsync(_db, ownerId);
        var e = EntitlementRules.Compute(snapshot, DateTime.UtcNow, await _settings.DefaultsAsync());
        _owners[ownerId] = e;
        return e;
    }

    public async Task<BranchEntitlements?> ForBranchAsync(int branchId)
    {
        if (_branches.TryGetValue(branchId, out var cached)) return cached;
        var ownerId = await _db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(b => b.UserId).FirstOrDefaultAsync();
        BranchEntitlements? result = null;
        if (ownerId != null)
        {
            var account = await ForOwnerAsync(ownerId);
            result = new BranchEntitlements(account, (await PausedBranchesAsync(ownerId)).Contains(branchId));
        }
        _branches[branchId] = result;
        return result;
    }

    public async Task<IReadOnlySet<int>> PausedBranchesAsync(string ownerId)
    {
        if (_paused.TryGetValue(ownerId, out var cached)) return cached;
        var max = (await ForOwnerAsync(ownerId)).MaxBranches;
        var live = await _db.Branches.AsNoTracking().Where(b => b.UserId == ownerId)
            .Select(b => new { b.Id, b.KeepActive }).ToListAsync();
        var paused = live.Count <= max ? new HashSet<int>() : EntitlementRules.PausedBranches(live.Select(b => (b.Id, b.KeepActive)), max);
        _paused[ownerId] = paused;
        return paused;
    }

    /// <summary>The owner's subscription as the rules see it; legacy terms when there is no row yet.</summary>
    public static async Task<SubscriptionSnapshot> SnapshotAsync(ApplicationDbContext db, string ownerId)
    {
        var sub = await db.Subscriptions.AsNoTracking().Include(s => s.Items).FirstOrDefaultAsync(s => s.OwnerId == ownerId);
        if (sub != null) return ToSnapshot(sub);

        var allowance = await db.Users.IgnoreQueryFilters().Where(u => u.Id == ownerId).Select(u => u.NumberOfBranches).FirstOrDefaultAsync();
        var live = await db.Branches.CountAsync(b => b.UserId == ownerId);
        return SubscriptionSnapshot.Legacy(Math.Max(allowance, live));
    }

    public static SubscriptionSnapshot ToSnapshot(Subscription s) => new(
        s.Status, s.IsLegacy,
        s.Items.GroupBy(i => i.Module).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity)),
        s.BranchQuantity, s.SeatsPerBranch, s.TrialEndsUtc, s.CurrentPeriodEndUtc, s.GraceEndsUtc, s.CancelAtPeriodEnd);
}
