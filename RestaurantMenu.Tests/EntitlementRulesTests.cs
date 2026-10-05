namespace RestaurantMenu.Tests;

public class EntitlementRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BillingDefaults Defaults = new(SeatsPerBranch: 5, GraceDays: 14);

    private static SubscriptionSnapshot Plan(SubscriptionStatus status, params BillingModule[] modules) =>
        new(status, false, modules.ToDictionary(m => m, _ => 1), BranchQuantity: 2);

    [Fact]
    public void Legacy_has_every_module_no_seat_limit_and_full_access()
    {
        var e = EntitlementRules.Compute(SubscriptionSnapshot.Legacy(3), Now, Defaults);
        Assert.Equal(AccountAccess.Full, e.Access);
        Assert.All(EntitlementRules.AllModules, m => Assert.True(e.Has(m), m.ToString()));
        Assert.Equal(3, e.MaxBranches);
        Assert.Null(e.SeatsPerBranch);
        Assert.Null(e.MaxOwnDomains);
    }

    [Fact]
    public void Legacy_ignores_stored_status_and_dates()
    {
        var s = SubscriptionSnapshot.Legacy(1) with { Status = SubscriptionStatus.ReadOnly, CurrentPeriodEndUtc = Now.AddDays(-100) };
        Assert.Equal(SubscriptionStatus.Active, EntitlementRules.EffectiveStatus(s, Now, Defaults));
    }

    [Fact]
    public void The_menu_is_always_included()
    {
        var e = EntitlementRules.Compute(Plan(SubscriptionStatus.Active), Now, Defaults);
        Assert.True(e.Has(BillingModule.Menu));
        Assert.False(e.Has(BillingModule.Bookings));
    }

    [Fact]
    public void A_module_without_its_base_is_off()
    {
        var e = EntitlementRules.Compute(Plan(SubscriptionStatus.Active, BillingModule.OwnDomain, BillingModule.Sms), Now, Defaults);
        Assert.False(e.Has(BillingModule.OwnDomain)); // needs Website
        Assert.False(e.Has(BillingModule.Sms));       // needs Bookings
        Assert.Equal(0, e.MaxOwnDomains);

        var with = EntitlementRules.Compute(Plan(SubscriptionStatus.Active, BillingModule.Website, BillingModule.OwnDomain), Now, Defaults);
        Assert.True(with.Has(BillingModule.OwnDomain));
        Assert.Equal(1, with.MaxOwnDomains);
    }

    [Fact]
    public void Trial_is_full_until_its_end_then_read_only()
    {
        var s = Plan(SubscriptionStatus.Trialing, BillingModule.Bookings) with { TrialEndsUtc = Now.AddDays(3) };
        var during = EntitlementRules.Compute(s, Now, Defaults);
        Assert.Equal(AccountAccess.Full, during.Access);
        Assert.Equal(3, during.TrialDaysLeft(Now));
        Assert.True(during.Has(BillingModule.Bookings));

        var atEnd = EntitlementRules.Compute(s, Now.AddDays(3), Defaults);
        Assert.Equal(SubscriptionStatus.ReadOnly, atEnd.Status);
        Assert.True(atEnd.TrialEnded);
        Assert.False(atEnd.CanWrite);
        Assert.False(atEnd.Has(BillingModule.Bookings));
        Assert.True(atEnd.Has(BillingModule.Menu));
        Assert.Contains("trial", EntitlementRules.Limitation(atEnd), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trial_days_round_up_and_never_go_negative()
    {
        var s = Plan(SubscriptionStatus.Trialing) with { TrialEndsUtc = Now.AddHours(1) };
        Assert.Equal(1, EntitlementRules.Compute(s, Now, Defaults).TrialDaysLeft(Now));
        Assert.Equal(0, EntitlementRules.Compute(s, Now, Defaults).TrialDaysLeft(Now.AddHours(2)));
    }

    [Fact]
    public void Paid_period_without_an_end_stays_active()
    {
        Assert.Equal(SubscriptionStatus.Active, EntitlementRules.EffectiveStatus(Plan(SubscriptionStatus.Active), Now.AddYears(5), Defaults));
    }

    [Fact]
    public void Ended_period_is_past_due_for_the_grace_days_then_read_only()
    {
        var s = Plan(SubscriptionStatus.Active, BillingModule.Ordering) with { CurrentPeriodEndUtc = Now };
        Assert.Equal(SubscriptionStatus.Active, EntitlementRules.EffectiveStatus(s, Now.AddSeconds(-1), Defaults));

        var pastDue = EntitlementRules.Compute(s, Now.AddDays(13), Defaults);
        Assert.Equal(SubscriptionStatus.PastDue, pastDue.Status);
        Assert.True(pastDue.CanWrite);
        Assert.True(pastDue.Has(BillingModule.Ordering));
        Assert.Equal(Now.AddDays(14), pastDue.GraceEndsUtc);
        Assert.NotNull(EntitlementRules.Limitation(pastDue));

        var after = EntitlementRules.Compute(s, Now.AddDays(14), Defaults);
        Assert.Equal(SubscriptionStatus.ReadOnly, after.Status);
        Assert.False(after.TrialEnded);
        Assert.False(after.Has(BillingModule.Ordering));
    }

    [Fact]
    public void A_stored_grace_end_wins_over_the_default()
    {
        var s = Plan(SubscriptionStatus.PastDue) with { CurrentPeriodEndUtc = Now, GraceEndsUtc = Now.AddDays(2) };
        Assert.Equal(SubscriptionStatus.PastDue, EntitlementRules.EffectiveStatus(s, Now.AddDays(1), Defaults));
        Assert.Equal(SubscriptionStatus.ReadOnly, EntitlementRules.EffectiveStatus(s, Now.AddDays(2), Defaults));
    }

    [Fact]
    public void Cancel_at_period_end_becomes_cancelled_not_past_due()
    {
        var s = Plan(SubscriptionStatus.Active) with { CurrentPeriodEndUtc = Now, CancelAtPeriodEnd = true };
        var e = EntitlementRules.Compute(s, Now.AddMinutes(1), Defaults);
        Assert.Equal(SubscriptionStatus.Cancelled, e.Status);
        Assert.False(e.CanWrite);
    }

    [Fact]
    public void Seats_come_from_the_plan_or_the_default()
    {
        Assert.Equal(5, EntitlementRules.Compute(Plan(SubscriptionStatus.Active), Now, Defaults).SeatsPerBranch);
        Assert.Equal(2, EntitlementRules.Compute(Plan(SubscriptionStatus.Active) with { SeatsPerBranch = 2 }, Now, Defaults).SeatsPerBranch);
    }

    [Fact]
    public void Over_the_limit_the_picked_branches_stay_then_the_oldest()
    {
        var live = new[] { (Id: 10, KeepActive: false), (Id: 11, KeepActive: false), (Id: 12, KeepActive: true), (Id: 13, KeepActive: false) };
        Assert.Equal(new HashSet<int> { 11, 13 }, EntitlementRules.PausedBranches(live, 2));
        Assert.Equal(new HashSet<int> { 10, 11, 13 }, EntitlementRules.PausedBranches(live, 1));
        Assert.Empty(EntitlementRules.PausedBranches(live, 4));
        Assert.Empty(EntitlementRules.PausedBranches(live, 10));
    }

    [Fact]
    public void A_paused_branch_keeps_only_the_menu_and_cant_be_changed()
    {
        var account = EntitlementRules.Compute(SubscriptionSnapshot.Legacy(1), Now, Defaults);
        var paused = new BranchEntitlements(account, Paused: true);
        Assert.False(paused.CanWrite);
        Assert.True(paused.Has(BillingModule.Menu));
        Assert.False(paused.Has(BillingModule.Ordering));
        Assert.True(new BranchEntitlements(account, Paused: false).Has(BillingModule.Ordering));
    }

    [Fact]
    public void Dependencies_are_only_own_domain_and_sms()
    {
        Assert.Equal(BillingModule.Website, EntitlementRules.Requires(BillingModule.OwnDomain));
        Assert.Equal(BillingModule.Bookings, EntitlementRules.Requires(BillingModule.Sms));
        Assert.All(EntitlementRules.AllModules.Except(new[] { BillingModule.OwnDomain, BillingModule.Sms }),
            m => Assert.Null(EntitlementRules.Requires(m)));
    }
}
