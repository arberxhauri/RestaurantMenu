using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>Whether the back office can change things, or only show them.</summary>
public enum AccountAccess { Full = 0, ReadOnly = 1 }

/// <summary>What the rules need from a subscription: no database types, so tests build them by hand.</summary>
public record SubscriptionSnapshot(
    SubscriptionStatus Status,
    bool IsLegacy,
    IReadOnlyDictionary<BillingModule, int> Items,
    int BranchQuantity,
    int? SeatsPerBranch = null,
    DateTime? TrialEndsUtc = null,
    DateTime? CurrentPeriodEndUtc = null,
    DateTime? GraceEndsUtc = null,
    bool CancelAtPeriodEnd = false)
{
    /// <summary>
    /// What an owner without a subscription row gets: the same as a legacy customer, so an owner
    /// made between the startup backfill and the next restart never loses anything.
    /// </summary>
    public static SubscriptionSnapshot Legacy(int branches) =>
        new(SubscriptionStatus.Active, true, EntitlementRules.AllModules.ToDictionary(m => m, _ => 0), Math.Max(branches, 0));
}

/// <summary>Defaults from configuration (Billing__…).</summary>
public record BillingDefaults(int SeatsPerBranch = 5, int GraceDays = 14);

/// <summary>What an account may do right now.</summary>
public record Entitlements(
    SubscriptionStatus Status,
    AccountAccess Access,
    IReadOnlySet<BillingModule> Modules,
    int MaxBranches,
    /// <summary>Staff per branch; null = no limit (legacy).</summary>
    int? SeatsPerBranch,
    /// <summary>Own domains across the account; null = no limit beyond the per-branch one (legacy).</summary>
    int? MaxOwnDomains,
    bool IsLegacy,
    DateTime? TrialEndsUtc,
    DateTime? GraceEndsUtc,
    /// <summary>Read-only because a trial ran out (not a lapsed or cancelled plan).</summary>
    bool TrialEnded = false)
{
    public bool Has(BillingModule module) => Modules.Contains(module);
    public bool CanWrite => Access == AccountAccess.Full;

    /// <summary>Whole days left in a trial (rounded up), or null when not trialing.</summary>
    public int? TrialDaysLeft(DateTime utcNow) =>
        Status == SubscriptionStatus.Trialing && TrialEndsUtc is { } end
            ? Math.Max(0, (int)Math.Ceiling((end - utcNow).TotalDays))
            : null;
}

/// <summary>What a branch may do: the account's modules, unless the branch is paused (over the plan's branch count).</summary>
public record BranchEntitlements(Entitlements Account, bool Paused)
{
    private static readonly IReadOnlySet<BillingModule> MenuOnly = new HashSet<BillingModule> { BillingModule.Menu };

    public IReadOnlySet<BillingModule> Modules => Paused ? MenuOnly : Account.Modules;
    public bool Has(BillingModule module) => Modules.Contains(module);
    public bool CanWrite => Account.CanWrite && !Paused;
}

/// <summary>
/// Plans → permissions, as pure functions (no database, no clock of their own), unit-tested in
/// EntitlementRulesTests. EntitlementService loads the data and calls these.
/// </summary>
public static class EntitlementRules
{
    public static readonly IReadOnlyList<BillingModule> AllModules = Enum.GetValues<BillingModule>();

    /// <summary>Modules that only make sense with another one.</summary>
    public static BillingModule? Requires(BillingModule module) => module switch
    {
        BillingModule.OwnDomain => BillingModule.Website,
        BillingModule.Sms => BillingModule.Bookings,
        _ => null
    };

    /// <summary>Billed per branch (quantity = the subscription's branch count).</summary>
    public static bool IsPerBranch(BillingModule module) => module is not (BillingModule.OwnDomain or BillingModule.Sms);

    public static string Name(BillingModule module) => module switch
    {
        BillingModule.Menu => "Menu",
        BillingModule.Ordering => "Table ordering",
        BillingModule.Bookings => "Bookings",
        BillingModule.Website => "Website",
        BillingModule.Management => "Management",
        BillingModule.OwnDomain => "Own domain",
        BillingModule.Sms => "Booking SMS",
        _ => module.ToString()
    };

    /// <summary>
    /// The status that applies at <paramref name="utcNow"/>. Without the billing worker (phase 3)
    /// nothing moves the stored status along, so time-based steps are worked out here:
    /// a trial past its end is read-only (nothing to be overdue on); a period past its end is
    /// cancelled if it was set to cancel, else past due until the grace period ends, then read-only.
    /// </summary>
    public static SubscriptionStatus EffectiveStatus(SubscriptionSnapshot s, DateTime utcNow, BillingDefaults d)
    {
        if (s.IsLegacy) return SubscriptionStatus.Active;
        switch (s.Status)
        {
            case SubscriptionStatus.Trialing:
                return s.TrialEndsUtc is { } trialEnd && utcNow >= trialEnd ? SubscriptionStatus.ReadOnly : SubscriptionStatus.Trialing;
            case SubscriptionStatus.Active:
                if (s.CurrentPeriodEndUtc is not { } end || utcNow < end) return SubscriptionStatus.Active;
                if (s.CancelAtPeriodEnd) return SubscriptionStatus.Cancelled;
                return utcNow < (s.GraceEndsUtc ?? end.AddDays(d.GraceDays)) ? SubscriptionStatus.PastDue : SubscriptionStatus.ReadOnly;
            case SubscriptionStatus.PastDue:
                var graceEnd = s.GraceEndsUtc ?? s.CurrentPeriodEndUtc?.AddDays(d.GraceDays);
                return graceEnd is { } g && utcNow >= g ? SubscriptionStatus.ReadOnly : SubscriptionStatus.PastDue;
            default:
                return s.Status;
        }
    }

    public static AccountAccess AccessFor(SubscriptionStatus status) =>
        status is SubscriptionStatus.ReadOnly or SubscriptionStatus.Cancelled ? AccountAccess.ReadOnly : AccountAccess.Full;

    public static Entitlements Compute(SubscriptionSnapshot s, DateTime utcNow, BillingDefaults d)
    {
        var status = EffectiveStatus(s, utcNow, d);
        var access = AccessFor(status);

        // Read-only: paid modules pause, the menu never does.
        var modules = new HashSet<BillingModule> { BillingModule.Menu };
        if (access == AccountAccess.Full)
        {
            // A module is on when the subscription has it (OwnDomain's quantity is how many domains).
            modules.UnionWith(s.Items.Keys);
            // A module whose base is off is off too (own domain without a website).
            modules.RemoveWhere(m => Requires(m) is { } needed && !modules.Contains(needed));
        }

        int? domains = s.IsLegacy ? null
            : modules.Contains(BillingModule.OwnDomain) ? Math.Max(0, s.Items.GetValueOrDefault(BillingModule.OwnDomain)) : 0;

        return new Entitlements(status, access, modules,
            MaxBranches: Math.Max(s.BranchQuantity, 0),
            SeatsPerBranch: s.IsLegacy ? null : Math.Max(s.SeatsPerBranch ?? d.SeatsPerBranch, 0),
            MaxOwnDomains: domains,
            IsLegacy: s.IsLegacy,
            TrialEndsUtc: status == SubscriptionStatus.Trialing ? s.TrialEndsUtc : null,
            GraceEndsUtc: status == SubscriptionStatus.PastDue ? s.GraceEndsUtc ?? s.CurrentPeriodEndUtc?.AddDays(d.GraceDays) : null,
            TrialEnded: s.Status == SubscriptionStatus.Trialing && status == SubscriptionStatus.ReadOnly);
    }

    /// <summary>
    /// Branches that are paused because the account has more live branches than it may: the
    /// owner's picks (KeepActive) stay first, then the oldest; everything past the limit pauses.
    /// </summary>
    public static IReadOnlySet<int> PausedBranches(IEnumerable<(int Id, bool KeepActive)> liveBranches, int maxBranches) =>
        liveBranches
            .OrderByDescending(b => b.KeepActive).ThenBy(b => b.Id)
            .Skip(Math.Max(maxBranches, 0))
            .Select(b => b.Id)
            .ToHashSet();

    /// <summary>One line for a banner: why the account is limited, or null when it isn't.</summary>
    public static string? Limitation(Entitlements e) => e.Status switch
    {
        SubscriptionStatus.ReadOnly when e.TrialEnded =>
            "Your free trial has ended, so the back office is read-only and paid features are paused. Your menus stay online.",
        SubscriptionStatus.ReadOnly =>
            "Your plan has ended, so the back office is read-only and paid features are paused. Your menus stay online.",
        SubscriptionStatus.Cancelled =>
            "Your subscription was cancelled, so the back office is read-only and paid features are paused. Your menus stay online.",
        SubscriptionStatus.PastDue =>
            $"Your payment is overdue. Everything keeps working until {e.GraceEndsUtc:d MMM yyyy}; after that the back office becomes read-only.",
        _ => null
    };
}
