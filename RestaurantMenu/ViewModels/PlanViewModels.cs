using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.ViewModels;

/// <summary>Admin → an owner's plan: what they have now, the form to change it, and the history.</summary>
public record PlanPage(
    ApplicationUser Owner,
    Subscription? Subscription,
    Entitlements Now,
    int LiveBranches,
    int Domains,
    int SeatsDefault,
    PlanForm Form,
    IReadOnlyDictionary<(BillingModule, BillingInterval), int> Prices,
    string Currency,
    IReadOnlyList<SubscriptionAudit> History,
    IReadOnlyDictionary<string, string> Actors,
    IReadOnlyList<string>? Errors = null);

/// <summary>The plan form. Dates are the owner's last day (inclusive), in UTC.</summary>
public class PlanForm
{
    public PlanKind Kind { get; set; } = PlanKind.Legacy;
    public List<BillingModule> Modules { get; set; } = new();
    public int Branches { get; set; } = 1;
    public int? SeatsPerBranch { get; set; }
    public int OwnDomains { get; set; } = 1;
    public BillingInterval Interval { get; set; } = BillingInterval.Month;
    public DateOnly? TrialEnds { get; set; }
    public DateOnly? PeriodEnds { get; set; }
    public uint? Version { get; set; }

    public static PlanForm From(Subscription? s, Entitlements now, int liveBranches)
    {
        if (s == null)
        {
            return new PlanForm { Kind = PlanKind.Legacy, Branches = Math.Max(1, now.MaxBranches), Modules = EntitlementRules.AllModules.ToList() };
        }
        return new PlanForm
        {
            Kind = s.IsLegacy ? PlanKind.Legacy
                : s.Status == SubscriptionStatus.Trialing ? PlanKind.Trial
                : s.Status is SubscriptionStatus.ReadOnly or SubscriptionStatus.Cancelled ? PlanKind.ReadOnly
                : PlanKind.Active,
            Modules = s.Items.Select(i => i.Module).ToList(),
            Branches = s.BranchQuantity,
            SeatsPerBranch = s.SeatsPerBranch,
            OwnDomains = Math.Max(1, s.Items.FirstOrDefault(i => i.Module == BillingModule.OwnDomain)?.Quantity ?? 1),
            Interval = s.Interval,
            TrialEnds = s.TrialEndsUtc is { } t ? DateOnly.FromDateTime(t.AddDays(-1)) : null,
            PeriodEnds = s.CurrentPeriodEndUtc is { } p ? DateOnly.FromDateTime(p.AddDays(-1)) : null,
            Version = s.Version
        };
    }

    /// <summary>A last day (inclusive) as the moment it ends: midnight UTC after it.</summary>
    public static DateTime? EndOf(DateOnly? lastDay) =>
        lastDay is { } d ? d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null;
}
