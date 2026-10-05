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

/// <summary>Admin → Plans &amp; prices: the plan settings, the price grid and the price history.</summary>
public record PricesPage(
    PlanSettingsForm Form,
    IReadOnlyDictionary<(BillingModule, BillingInterval), int> CurrentPrices,
    IReadOnlyList<PriceBook> History,
    IReadOnlyDictionary<string, string> Actors,
    DateTime UpdatedUtc,
    string? UpdatedBy,
    bool EmailWorks,
    string? EmailProblem,
    IReadOnlyList<string>? Errors = null);

/// <summary>The Plans &amp; prices form. Prices are typed as text (15, 15.50, 15,50); empty = no price.</summary>
public class PlanSettingsForm
{
    public string Currency { get; set; } = "EUR";
    public int TrialDays { get; set; } = 14;
    public int SeatsPerBranch { get; set; } = 5;
    public int GraceDays { get; set; } = 14;
    // Checkboxes: an unticked box posts nothing, so both must default to false here (the page
    // is always filled from the saved settings by From).
    public bool SignupEnabled { get; set; }
    public bool SignupRequireApproval { get; set; }
    // Invoices: the seller and the rules.
    public string? OperatorName { get; set; }
    public string? OperatorNipt { get; set; }
    public string? OperatorAddress { get; set; }
    public string? OperatorEmail { get; set; }
    public string? OperatorIban { get; set; }
    public string? OperatorBank { get; set; }
    public string? OperatorSwift { get; set; }
    public string? VatPercent { get; set; } = "0";
    public int InvoiceDueDays { get; set; } = 14;
    public int RenewalLeadDays { get; set; } = 7;
    public uint? Version { get; set; }
    /// <summary>"Ordering_Month" → "9.00".</summary>
    public Dictionary<string, string?> Prices { get; set; } = new();

    public static string Key(BillingModule m, BillingInterval i) => $"{m}_{i}";

    public static PlanSettingsForm From(PlanSettings s, IReadOnlyDictionary<(BillingModule, BillingInterval), int> prices) => new()
    {
        Currency = s.Currency,
        TrialDays = s.TrialDays,
        SeatsPerBranch = s.SeatsPerBranch,
        GraceDays = s.GraceDays,
        SignupEnabled = s.SignupEnabled,
        SignupRequireApproval = s.SignupRequireApproval,
        OperatorName = s.OperatorName, OperatorNipt = s.OperatorNipt, OperatorAddress = s.OperatorAddress, OperatorEmail = s.OperatorEmail,
        OperatorIban = s.OperatorIban, OperatorBank = s.OperatorBank, OperatorSwift = s.OperatorSwift,
        VatPercent = s.VatPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        InvoiceDueDays = s.InvoiceDueDays, RenewalLeadDays = s.RenewalLeadDays,
        Version = s.Version == 0 ? null : s.Version,
        Prices = prices.ToDictionary(p => Key(p.Key.Item1, p.Key.Item2), p => (string?)Helpers.MoneyInput.Format(p.Value))
    };
}
