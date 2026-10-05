namespace RestaurantMenu.Tests;

public class PricingRulesTests
{
    // Cents per unit: menu and modules per branch per month; own domain per domain.
    private static readonly Dictionary<(BillingModule, BillingInterval), int> Prices = new()
    {
        [(BillingModule.Menu, BillingInterval.Month)] = 1500,
        [(BillingModule.Menu, BillingInterval.Year)] = 15000,
        [(BillingModule.Ordering, BillingInterval.Month)] = 900,
        [(BillingModule.Ordering, BillingInterval.Year)] = 9000,
        [(BillingModule.Bookings, BillingInterval.Month)] = 700,
        [(BillingModule.Bookings, BillingInterval.Year)] = 7000,
        [(BillingModule.Website, BillingInterval.Month)] = 500,
        [(BillingModule.Website, BillingInterval.Year)] = 5000,
        [(BillingModule.Management, BillingInterval.Month)] = 1200,
        [(BillingModule.Management, BillingInterval.Year)] = 12000,
        [(BillingModule.OwnDomain, BillingInterval.Month)] = 300,
        [(BillingModule.OwnDomain, BillingInterval.Year)] = 3000
    };

    [Fact]
    public void The_menu_is_always_in_and_alone_by_default()
    {
        var s = PricingRules.Parse(null, null, null);
        Assert.Equal(new HashSet<BillingModule> { BillingModule.Menu }, s.Modules);
        Assert.Equal(1, s.Branches);
        Assert.Equal(BillingInterval.Month, s.Interval);
    }

    [Fact]
    public void Each_preset_has_the_expected_monthly_price_for_one_branch()
    {
        int Total(string preset) => PricingRules.Quote(PricingRules.Parse(null, "1", "Month", preset), Prices, "EUR").TotalCents;
        Assert.Equal(1500, Total("menu"));
        Assert.Equal(1500 + 500 + 700, Total("guests"));
        Assert.Equal(1500 + 900 + 700 + 500 + 1200, Total("everything"));
    }

    [Fact]
    public void Per_branch_modules_multiply_and_an_own_domain_does_not()
    {
        var s = PricingRules.Parse(new[] { "Website", "OwnDomain", "Ordering" }, "3", "Month");
        var q = PricingRules.Quote(s, Prices, "EUR");
        Assert.Equal(3 * 1500 + 3 * 500 + 3 * 900 + 300, q.TotalCents);
        Assert.Equal(1, q.Lines.Single(l => l.Module == BillingModule.OwnDomain).Quantity);
        Assert.Equal(3, q.Lines.Single(l => l.Module == BillingModule.Website).Quantity);
    }

    [Fact]
    public void Yearly_uses_its_own_price_and_shows_the_saving()
    {
        var q = PricingRules.Quote(PricingRules.Parse(new[] { "Ordering" }, "2", "Year"), Prices, "EUR");
        Assert.Equal(2 * 15000 + 2 * 9000, q.TotalCents);
        Assert.Equal(2 * (1500 + 900) * 12, q.MonthlyForAYearCents);
        Assert.Equal(2 * (1500 + 900) * 12 - q.TotalCents, q.YearlySavingCents);
        Assert.Equal((int)Math.Round(q.TotalCents / 12m), q.PerMonthCents);
    }

    [Fact]
    public void Monthly_never_shows_a_yearly_saving()
    {
        Assert.Null(PricingRules.Quote(PricingRules.Parse(null, "1", "Month"), Prices, "EUR").YearlySavingCents);
    }

    [Fact]
    public void A_dependent_module_without_its_base_is_dropped()
    {
        Assert.DoesNotContain(BillingModule.OwnDomain, PricingRules.Parse(new[] { "OwnDomain" }, "1", "Month").Modules);
        Assert.Contains(BillingModule.OwnDomain, PricingRules.Parse(new[] { "OwnDomain,Website" }, "1", "Month").Modules);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-4", 1)]
    [InlineData("abc", 1)]
    [InlineData("7", 7)]
    [InlineData("500", PlanSelection.MaxBranches)]
    public void Branch_count_is_kept_in_range(string raw, int expected)
    {
        Assert.Equal(expected, PricingRules.Parse(null, raw, null).Branches);
    }

    [Fact]
    public void Unknown_modules_and_sms_are_ignored()
    {
        var s = PricingRules.Parse(new[] { "Teleport", "Sms", "bookings" }, "1", "Month");
        Assert.Equal(new HashSet<BillingModule> { BillingModule.Menu, BillingModule.Bookings }, s.Modules);
    }

    [Fact]
    public void A_missing_price_makes_the_quote_incomplete()
    {
        var partial = new Dictionary<(BillingModule, BillingInterval), int> { [(BillingModule.Menu, BillingInterval.Month)] = 1500 };
        var q = PricingRules.Quote(PricingRules.Parse(new[] { "Bookings" }, "1", "Month"), partial, "EUR");
        Assert.False(q.Complete);
        Assert.Null(q.Lines.Single(l => l.Module == BillingModule.Bookings).TotalCents);
        Assert.Null(q.YearlySavingCents);
    }

    [Fact]
    public void The_selection_round_trips_through_its_query()
    {
        var s = PricingRules.Parse(new[] { "Website", "Bookings" }, "2", "Year");
        var query = System.Web.HttpUtility.ParseQueryString(s.ToQuery());
        var back = PricingRules.Parse(query.GetValues("m"), query["b"], query["i"]);
        Assert.True(s.Modules.SetEquals(back.Modules));
        Assert.Equal(s.Branches, back.Branches);
        Assert.Equal(s.Interval, back.Interval);
    }

    [Theory]
    [InlineData(1500, "EUR", "€15")]
    [InlineData(1550, "EUR", "€15.50")]
    [InlineData(150000, "ALL", "1,500 ALL")]
    public void Money_is_formatted_without_the_servers_culture(int cents, string currency, string text)
    {
        Assert.Equal(text, PricingRules.Money(cents, currency));
    }
}
