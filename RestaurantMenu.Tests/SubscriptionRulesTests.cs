namespace RestaurantMenu.Tests;

public class SubscriptionRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private const int Grace = 14;

    private static LifecycleState Trial(DateTime ends) => new(SubscriptionStatus.Trialing, false, ends, null, null, null, false);
    private static LifecycleState Active(DateTime start, DateTime end) => new(SubscriptionStatus.Active, false, null, start, end, null, false);

    [Fact]
    public void Periods_are_a_calendar_month_or_year()
    {
        Assert.Equal(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc), SubscriptionRules.PeriodEnd(Now, BillingInterval.Month));
        Assert.Equal(new DateTime(2027, 10, 5, 12, 0, 0, DateTimeKind.Utc), SubscriptionRules.PeriodEnd(Now, BillingInterval.Year));
        // Jan 31 + 1 month = Feb 28 (no overflow into March).
        Assert.Equal(new DateTime(2027, 2, 28), SubscriptionRules.PeriodEnd(new DateTime(2027, 1, 31), BillingInterval.Month));
    }

    [Fact]
    public void A_first_invoice_starts_when_a_running_trial_ends_else_now()
    {
        Assert.Equal(Now.AddDays(5), SubscriptionRules.FirstPeriodStart(Trial(Now.AddDays(5)), Now));
        Assert.Equal(Now, SubscriptionRules.FirstPeriodStart(Trial(Now.AddDays(-1)) with { Status = SubscriptionStatus.ReadOnly }, Now));
    }

    [Fact]
    public void Trial_ends_unpaid_then_read_only_with_the_trial_ended_email()
    {
        var s = Trial(Now);
        Assert.Equal(SubscriptionStatus.Trialing, SubscriptionRules.Advance(s, Now.AddSeconds(-1), Grace).Status);
        var after = SubscriptionRules.Advance(s, Now, Grace);
        Assert.Equal(SubscriptionStatus.ReadOnly, after.Status);
        Assert.Equal("trial-ended", SubscriptionRules.TransitionEmail(s.Status, after.Status));
    }

    [Fact]
    public void Payment_makes_any_state_active_for_the_invoice_period()
    {
        var readOnly = Trial(Now.AddDays(-3)) with { Status = SubscriptionStatus.ReadOnly };
        var paid = SubscriptionRules.ApplyPayment(readOnly, Now, Now.AddMonths(1));
        Assert.Equal(SubscriptionStatus.Active, paid.Status);
        Assert.Equal(Now, paid.CurrentPeriodStartUtc);
        Assert.Equal(Now.AddMonths(1), paid.CurrentPeriodEndUtc);
        Assert.Null(paid.GraceEndsUtc);
        Assert.Equal(SubscriptionStatus.Active, SubscriptionRules.Advance(paid, Now.AddDays(10), Grace).Status);
    }

    [Fact]
    public void Paying_during_a_trial_keeps_the_trial_and_runs_on_after_it()
    {
        var trial = Trial(Now.AddDays(4));
        var paid = SubscriptionRules.ApplyPayment(trial, Now.AddDays(4), Now.AddDays(4).AddMonths(1));
        Assert.Equal(SubscriptionStatus.Active, paid.Status);
        Assert.Equal(Now.AddDays(4).AddMonths(1), paid.CurrentPeriodEndUtc);
        Assert.Equal(SubscriptionStatus.Active, SubscriptionRules.Advance(paid, Now.AddDays(20), Grace).Status);
    }

    [Fact]
    public void Paying_an_older_invoice_never_shortens_a_later_paid_end()
    {
        var s = Active(Now, Now.AddMonths(2));
        var paid = SubscriptionRules.ApplyPayment(s, Now, Now.AddMonths(1));
        Assert.Equal(Now.AddMonths(2), paid.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Unpaid_renewal_is_past_due_for_the_grace_days_then_read_only()
    {
        var s = Active(Now.AddMonths(-1), Now);
        var pastDue = SubscriptionRules.Advance(s, Now.AddHours(1), Grace);
        Assert.Equal(SubscriptionStatus.PastDue, pastDue.Status);
        Assert.Equal(Now.AddDays(Grace), pastDue.GraceEndsUtc);
        Assert.Equal("past-due", SubscriptionRules.TransitionEmail(s.Status, pastDue.Status));

        // The grace end is fixed when it's announced: a later change of grace days doesn't move it.
        Assert.Equal(Now.AddDays(Grace), SubscriptionRules.Advance(pastDue, Now.AddDays(2), 30).GraceEndsUtc);

        Assert.Equal(SubscriptionStatus.PastDue, SubscriptionRules.Advance(pastDue, Now.AddDays(Grace).AddSeconds(-1), Grace).Status);
        var readOnly = SubscriptionRules.Advance(pastDue, Now.AddDays(Grace), Grace);
        Assert.Equal(SubscriptionStatus.ReadOnly, readOnly.Status);
        Assert.Equal("read-only", SubscriptionRules.TransitionEmail(pastDue.Status, readOnly.Status));

        // Paying then brings it straight back.
        Assert.Equal(SubscriptionStatus.Active, SubscriptionRules.ApplyPayment(readOnly, Now, Now.AddMonths(1)).Status);
    }

    [Fact]
    public void Cancelled_at_period_end_becomes_cancelled_not_past_due()
    {
        var s = Active(Now.AddMonths(-1), Now) with { CancelAtPeriodEnd = true };
        var after = SubscriptionRules.Advance(s, Now.AddMinutes(1), Grace);
        Assert.Equal(SubscriptionStatus.Cancelled, after.Status);
        Assert.Equal("cancelled", SubscriptionRules.TransitionEmail(s.Status, after.Status));
    }

    [Fact]
    public void Legacy_never_moves()
    {
        var s = Active(Now.AddYears(-2), Now.AddYears(-1)) with { IsLegacy = true };
        Assert.Equal(s, SubscriptionRules.Advance(s, Now, Grace));
        Assert.False(SubscriptionRules.RenewalDue(s, Now, 7));
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(7, "trial-7")]
    [InlineData(5, "trial-7")]
    [InlineData(3, "trial-3")]
    [InlineData(1, "trial-1")]
    [InlineData(0.2, "trial-1")]
    [InlineData(-1, null)]
    public void Trial_reminders_at_7_3_and_1_days(double daysLeft, string? kind)
    {
        Assert.Equal(kind, SubscriptionRules.TrialReminder(Now.AddDays(daysLeft), Now));
    }

    [Fact]
    public void Grace_and_invoice_reminders_in_the_last_three_days()
    {
        Assert.Null(SubscriptionRules.GraceReminder(Now.AddDays(5), Now));
        Assert.Equal("grace-3", SubscriptionRules.GraceReminder(Now.AddDays(2), Now));
        Assert.Null(SubscriptionRules.GraceReminder(Now.AddDays(-1), Now));

        Assert.Equal("due-3", SubscriptionRules.InvoiceReminder(Now.AddDays(2), Now.AddDays(-12), Now));
        Assert.Null(SubscriptionRules.InvoiceReminder(Now.AddDays(5), Now.AddDays(-9), Now));
        // Issued only two days before it's due: the invoice email itself is the reminder.
        Assert.Null(SubscriptionRules.InvoiceReminder(Now.AddDays(2), Now, Now));
    }

    [Fact]
    public void Renewal_invoices_go_out_the_lead_days_before_the_end()
    {
        var s = Active(Now.AddDays(-20), Now.AddDays(10));
        Assert.False(SubscriptionRules.RenewalDue(s, Now, 7));
        Assert.True(SubscriptionRules.RenewalDue(s, Now.AddDays(3), 7));
        Assert.False(SubscriptionRules.RenewalDue(s with { CancelAtPeriodEnd = true }, Now.AddDays(3), 7));
        Assert.False(SubscriptionRules.RenewalDue(Trial(Now.AddDays(3)), Now, 7));
    }
}

public class InvoiceRulesTests
{
    [Fact]
    public void Numbers_are_per_year_and_padded()
    {
        Assert.Equal("MQM-2026-0001", InvoiceRules.Number(2026, 1));
        Assert.Equal("MQM-2026-0042", InvoiceRules.Number(2026, 42));
        Assert.Equal("MQM-2027-12345", InvoiceRules.Number(2027, 12345));
    }

    [Fact]
    public void Ibans_print_in_groups_of_four()
    {
        Assert.Equal("AL47 2121 1009 0000 0002 3569 8741", InvoiceRules.FormatIban("al47212110090000000235698741"));
        Assert.Equal("", InvoiceRules.FormatIban(null));
    }

    [Theory]
    [InlineData(1500, "EUR", false, "€15")]
    [InlineData(1500, "EUR", true, "€15.00")]
    [InlineData(150000, "EUR", true, "€1,500.00")]
    [InlineData(150000, "ALL", false, "1,500 ALL")]
    public void Invoices_always_show_cents(int cents, string currency, bool always, string text)
    {
        Assert.Equal(text, PricingRules.Money(cents, currency, always));
    }

    [Theory]
    [InlineData(10000, 0, 0)]
    [InlineData(10000, 20, 2000)]
    [InlineData(1995, 20, 399)]
    [InlineData(1, 50, 1)]   // 0.5 rounds away from zero
    public void Vat_rounds_to_whole_cents(int subtotal, int percent, int vat)
    {
        Assert.Equal(vat, InvoiceRules.Vat(subtotal, percent));
    }

    [Fact]
    public void Lines_price_each_module_and_report_the_unpriced()
    {
        var prices = new Dictionary<(BillingModule, BillingInterval), int>
        {
            [(BillingModule.Menu, BillingInterval.Month)] = 1500,
            [(BillingModule.Bookings, BillingInterval.Month)] = 700
        };
        var plan = new PlanSelection(new HashSet<BillingModule> { BillingModule.Menu, BillingModule.Bookings, BillingModule.Website }, 2, BillingInterval.Month);
        var (lines, unpriced) = InvoiceRules.Lines(plan, prices, "EUR");
        Assert.Equal(new[] { BillingModule.Website }, unpriced);
        Assert.Equal(2 * 1500 + 2 * 700, lines.Sum(l => l.TotalCents));
    }

    [Fact]
    public void The_next_invoice_uses_a_pending_change_and_any_branch_count()
    {
        var items = new[] { BillingModule.Menu, BillingModule.Ordering, BillingModule.Sms };
        var current = InvoiceRules.PlanFor(items, 80, BillingInterval.Month, null, null, null);
        Assert.Equal(80, current.Branches); // not capped like the pricing page
        Assert.DoesNotContain(BillingModule.Sms, current.Modules);

        var next = InvoiceRules.PlanFor(items, 2, BillingInterval.Month, "Bookings,OwnDomain", 3, BillingInterval.Year);
        Assert.Equal(new HashSet<BillingModule> { BillingModule.Menu, BillingModule.Bookings }, next.Modules); // own domain needs the website
        Assert.Equal(3, next.Branches);
        Assert.Equal(BillingInterval.Year, next.Interval);
    }
}
