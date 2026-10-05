using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>The parts of a subscription the lifecycle changes, as plain values.</summary>
public record LifecycleState(
    SubscriptionStatus Status,
    bool IsLegacy,
    DateTime? TrialEndsUtc,
    DateTime? CurrentPeriodStartUtc,
    DateTime? CurrentPeriodEndUtc,
    DateTime? GraceEndsUtc,
    bool CancelAtPeriodEnd);

/// <summary>
/// The subscription lifecycle as pure functions (tested in SubscriptionRulesTests with fixed
/// clocks). BillingWorker and BillingService load and save; these decide.
///
/// Trialing → (paid) Active → (period ends unpaid) PastDue → (grace ends) ReadOnly.
/// Trialing → (trial ends unpaid) ReadOnly. Active + cancel at period end → Cancelled.
/// A payment brings any of them back to Active, except Cancelled needs a new checkout.
/// </summary>
public static class SubscriptionRules
{
    /// <summary>The end of a billing period that starts at <paramref name="start"/>.</summary>
    public static DateTime PeriodEnd(DateTime start, BillingInterval interval) =>
        interval == BillingInterval.Year ? start.AddYears(1) : start.AddMonths(1);

    /// <summary>
    /// Where a first invoice's period starts: when a running trial ends (the trial is never cut
    /// short), else now. For a renewal it's the end of the paid period.
    /// </summary>
    public static DateTime FirstPeriodStart(LifecycleState s, DateTime utcNow) =>
        s.Status == SubscriptionStatus.Trialing && s.TrialEndsUtc is { } end && end > utcNow ? end : utcNow;

    /// <summary>
    /// A paid invoice: the account is Active for the invoice's period (or keeps a later paid end
    /// it already has), grace and cancellation are cleared.
    /// </summary>
    public static LifecycleState ApplyPayment(LifecycleState s, DateTime periodStart, DateTime periodEnd) => s with
    {
        Status = SubscriptionStatus.Active,
        IsLegacy = false,
        CurrentPeriodStartUtc = s.CurrentPeriodEndUtc is { } had && had >= periodEnd ? s.CurrentPeriodStartUtc : periodStart,
        CurrentPeriodEndUtc = s.CurrentPeriodEndUtc is { } had2 && had2 >= periodEnd ? had2 : periodEnd,
        GraceEndsUtc = null,
        CancelAtPeriodEnd = false
    };

    /// <summary>
    /// The status to store now. Same rules as the entitlements (EntitlementRules.EffectiveStatus),
    /// plus the grace end fixed when an account first falls past due, so later changes to the
    /// grace days don't move a deadline already announced.
    /// </summary>
    public static LifecycleState Advance(LifecycleState s, DateTime utcNow, int graceDays)
    {
        if (s.IsLegacy) return s;
        var snapshot = new SubscriptionSnapshot(s.Status, false, new Dictionary<BillingModule, int>(), 1,
            TrialEndsUtc: s.TrialEndsUtc, CurrentPeriodEndUtc: s.CurrentPeriodEndUtc, GraceEndsUtc: s.GraceEndsUtc, CancelAtPeriodEnd: s.CancelAtPeriodEnd);
        var status = EntitlementRules.EffectiveStatus(snapshot, utcNow, new BillingDefaults(0, graceDays));
        var grace = status == SubscriptionStatus.PastDue && s.GraceEndsUtc == null && s.CurrentPeriodEndUtc is { } end
            ? end.AddDays(graceDays) : s.GraceEndsUtc;
        return s with { Status = status, GraceEndsUtc = grace };
    }

    /// <summary>The email a status change calls for, or null: past-due, read-only, trial-ended, cancelled.</summary>
    public static string? TransitionEmail(SubscriptionStatus from, SubscriptionStatus to) => (from, to) switch
    {
        (_, _) when from == to => null,
        (_, SubscriptionStatus.PastDue) => "past-due",
        (SubscriptionStatus.Trialing, SubscriptionStatus.ReadOnly) => "trial-ended",
        (_, SubscriptionStatus.ReadOnly) => "read-only",
        (_, SubscriptionStatus.Cancelled) => "cancelled",
        _ => null
    };

    /// <summary>
    /// The trial reminder due now: "trial-7", "trial-3" or "trial-1" (whole days left, rounded up),
    /// or null. If the worker was down at the 7-day mark, the 3-day one still goes out.
    /// </summary>
    public static string? TrialReminder(DateTime? trialEnd, DateTime utcNow)
    {
        if (trialEnd is not { } end || end <= utcNow) return null;
        var days = (int)Math.Ceiling((end - utcNow).TotalDays);
        return days <= 1 ? "trial-1" : days <= 3 ? "trial-3" : days <= 7 ? "trial-7" : null;
    }

    /// <summary>"grace-3" when an account past due has 3 days or less before it turns read-only.</summary>
    public static string? GraceReminder(DateTime? graceEnd, DateTime utcNow) =>
        graceEnd is { } g && g > utcNow && g - utcNow <= TimeSpan.FromDays(3) ? "grace-3" : null;

    /// <summary>"due-3" when an open invoice is due within 3 days (and not yet overdue).</summary>
    public static string? InvoiceReminder(DateTime due, DateTime issued, DateTime utcNow) =>
        due > utcNow && due - utcNow <= TimeSpan.FromDays(3) && due - issued > TimeSpan.FromDays(3) ? "due-3" : null;

    /// <summary>A renewal invoice is due when the paid period ends within the lead days (and isn't cancelling).</summary>
    public static bool RenewalDue(LifecycleState s, DateTime utcNow, int leadDays) =>
        !s.IsLegacy && !s.CancelAtPeriodEnd && s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue
        && s.CurrentPeriodEndUtc is { } end && utcNow >= end.AddDays(-Math.Max(0, leadDays));

    /// <summary>A key for "this period" in the email log, e.g. 2026-10-19.</summary>
    public static string Key(DateTime? utc) => utc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
}

/// <summary>Invoice sums and numbering (tested in InvoiceRulesTests).</summary>
public static class InvoiceRules
{
    public static string Number(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"MQM-{year}-{sequence:0000}");

    /// <summary>An IBAN the way banks print it: groups of four ("AL47 2121 1009 …").</summary>
    public static string FormatIban(string? iban)
    {
        var raw = (iban ?? "").Replace(" ", "").ToUpperInvariant();
        return string.Join(' ', Enumerable.Range(0, (raw.Length + 3) / 4).Select(i => raw.Substring(i * 4, Math.Min(4, raw.Length - i * 4))));
    }

    /// <summary>VAT on a subtotal, rounded half away from zero to whole cents.</summary>
    public static int Vat(int subtotalCents, decimal vatPercent) =>
        vatPercent <= 0 ? 0 : (int)Math.Round(subtotalCents * vatPercent / 100m, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The lines for a plan, priced from the current price list; the modules with no price are
    /// returned instead (such a plan can't be invoiced: "price on request").
    /// </summary>
    public static (List<QuoteLine> Lines, List<BillingModule> Unpriced) Lines(PlanSelection plan,
        IReadOnlyDictionary<(BillingModule, BillingInterval), int> prices, string currency)
    {
        var quote = PricingRules.Quote(plan, prices, currency);
        return (quote.Lines.Where(l => l.UnitCents != null).ToList(), quote.Lines.Where(l => l.UnitCents == null).Select(l => l.Module).ToList());
    }

    /// <summary>The modules a subscription will bill: its pending change if any, else its items.</summary>
    public static PlanSelection PlanFor(IEnumerable<BillingModule> items, int branches, BillingInterval interval,
        string? nextModules, int? nextBranches, BillingInterval? nextInterval)
    {
        // Not PricingRules.Parse: an admin's plan may have more branches than the pricing page offers.
        var picked = new HashSet<BillingModule> { BillingModule.Menu };
        if (nextModules != null)
        {
            foreach (var part in nextModules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse<BillingModule>(part, true, out var m) && m != BillingModule.Sms) picked.Add(m);
        }
        else
        {
            picked.UnionWith(items.Where(m => m != BillingModule.Sms));
        }
        picked.RemoveWhere(m => EntitlementRules.Requires(m) is { } needed && !picked.Contains(needed));
        return new PlanSelection(picked, Math.Max(1, nextBranches ?? branches), nextInterval ?? interval);
    }
}
