using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>What a visitor picked on the pricing page: modules, branches, monthly or yearly.</summary>
public record PlanSelection(IReadOnlySet<BillingModule> Modules, int Branches, BillingInterval Interval)
{
    public const int MaxBranches = 50;

    /// <summary>The query string that carries a selection from /pricing to /signup: m=Ordering&amp;m=Bookings&amp;b=2&amp;i=Year.</summary>
    public string ToQuery() =>
        string.Join("&", Modules.Where(m => m != BillingModule.Menu).OrderBy(m => m).Select(m => "m=" + m)
            .Append("b=" + Branches.ToString(CultureInfo.InvariantCulture))
            .Append("i=" + Interval));
}

/// <summary>One line of a quote. <see cref="UnitCents"/> null: no price set for this module yet.</summary>
public record QuoteLine(BillingModule Module, int Quantity, int? UnitCents)
{
    public int? TotalCents => UnitCents * Quantity;
}

/// <summary>
/// A price for a selection, per billing period (month or year). <see cref="Complete"/> is false
/// when a module has no price yet; the page then says "price on request" instead of a total.
/// </summary>
public record Quote(PlanSelection Selection, IReadOnlyList<QuoteLine> Lines, string Currency)
{
    public bool Complete => Lines.All(l => l.UnitCents != null);
    public int TotalCents => Lines.Sum(l => l.TotalCents ?? 0);
    /// <summary>What the same selection costs per year when paid monthly, to show the yearly saving.</summary>
    public int? MonthlyForAYearCents { get; init; }
    public int? YearlySavingCents => Selection.Interval == BillingInterval.Year && Complete && MonthlyForAYearCents is { } m && m > TotalCents
        ? m - TotalCents : null;
    /// <summary>The total per month (a yearly price divided by 12), for "from €X a month".</summary>
    public int PerMonthCents => Selection.Interval == BillingInterval.Year ? (int)Math.Round(TotalCents / 12m) : TotalCents;
}

/// <summary>
/// The pricing page's sums, as pure functions over a price table (tested in PricingRulesTests).
/// A subscription is the menu plus the modules picked; per-branch modules are multiplied by the
/// branch count, an own domain is one per domain, and booking SMS isn't sold yet.
/// </summary>
public static class PricingRules
{
    /// <summary>What can be picked on the pricing page (booking SMS comes with SMS packs later).</summary>
    public static readonly IReadOnlyList<BillingModule> Sellable = new[]
    {
        BillingModule.Ordering, BillingModule.Bookings, BillingModule.Website, BillingModule.Management, BillingModule.OwnDomain
    };

    /// <summary>Starting selections; one click fills the boxes. Never a separate plan in the data.</summary>
    public static readonly IReadOnlyList<(string Key, BillingModule[] Modules)> Presets = new[]
    {
        ("menu", Array.Empty<BillingModule>()),
        ("guests", new[] { BillingModule.Website, BillingModule.Bookings }),
        ("everything", new[] { BillingModule.Ordering, BillingModule.Bookings, BillingModule.Website, BillingModule.Management })
    };

    /// <summary>
    /// A selection from query or form values: unknown modules and SMS are dropped, the menu is
    /// always in, a module whose base is missing is dropped (own domain without a website), and
    /// the branch count is kept between 1 and <see cref="PlanSelection.MaxBranches"/>.
    /// </summary>
    public static PlanSelection Parse(IEnumerable<string?>? modules, string? branches, string? interval, string? preset = null)
    {
        var picked = new HashSet<BillingModule> { BillingModule.Menu };
        if (preset != null && Presets.FirstOrDefault(p => p.Key == preset) is { Key: not null } chosen)
        {
            picked.UnionWith(chosen.Modules);
        }
        else
        {
            foreach (var raw in modules ?? Enumerable.Empty<string?>())
            {
                foreach (var part in (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Enum.TryParse<BillingModule>(part, true, out var m) && Sellable.Contains(m)) picked.Add(m);
                }
            }
        }
        picked.RemoveWhere(m => EntitlementRules.Requires(m) is { } needed && !picked.Contains(needed));

        var count = int.TryParse(branches, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : 1;
        var period = string.Equals(interval, "Year", StringComparison.OrdinalIgnoreCase) ? BillingInterval.Year : BillingInterval.Month;
        return new PlanSelection(picked, Math.Clamp(count, 1, PlanSelection.MaxBranches), period);
    }

    /// <summary>
    /// The price of a selection for one billing period. <paramref name="prices"/> are unit prices
    /// in cents by (module, interval); a missing price leaves its line unpriced.
    /// </summary>
    public static Quote Quote(PlanSelection s, IReadOnlyDictionary<(BillingModule, BillingInterval), int> prices, string currency)
    {
        static int Quantity(BillingModule m, PlanSelection s) => EntitlementRules.IsPerBranch(m) ? s.Branches : 1;

        var lines = s.Modules.OrderBy(m => m)
            .Select(m => new QuoteLine(m, Quantity(m, s), prices.TryGetValue((m, s.Interval), out var c) ? c : null))
            .ToList();

        int? monthlyForAYear = null;
        if (s.Interval == BillingInterval.Year)
        {
            var monthly = s.Modules.Select(m => prices.TryGetValue((m, BillingInterval.Month), out var c) ? c * Quantity(m, s) : (int?)null).ToList();
            if (monthly.All(x => x != null)) monthlyForAYear = monthly.Sum(x => x!.Value) * 12;
        }
        return new Quote(s, lines, currency) { MonthlyForAYearCents = monthlyForAYear };
    }

    /// <summary>
    /// "€15" or "€15.50" for EUR, "1,500 ALL" otherwise. Invariant digits so the page never depends
    /// on the server's culture. <paramref name="alwaysCents"/>: invoices and amounts due show "€15.00".
    /// </summary>
    public static string Money(int cents, string currency, bool alwaysCents = false)
    {
        var whole = cents % 100 == 0 && !alwaysCents;
        return currency switch
        {
            "EUR" => "€" + (cents / 100m).ToString(whole ? "#,0" : "#,0.00", CultureInfo.InvariantCulture),
            "USD" => "$" + (cents / 100m).ToString(whole ? "#,0" : "#,0.00", CultureInfo.InvariantCulture),
            _ => (cents / 100m).ToString(whole ? "#,0" : "#,0.00", CultureInfo.InvariantCulture) + " " + currency
        };
    }
}
