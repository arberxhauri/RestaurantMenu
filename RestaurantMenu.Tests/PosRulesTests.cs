namespace RestaurantMenu.Tests;

/// <summary>
/// The POS's money rules: when a discount applies and how much it takes off, the bill's totals,
/// whether a payment covers the bill (and the change), and how a bill is addressed.
/// </summary>
public class PosRulesTests
{
    private static readonly DateTime Friday8pm = new(2026, 11, 27, 20, 0, 0); // a Friday

    private static Discount Pct(decimal value, DiscountScope scope = DiscountScope.Bill, int? product = null) =>
        new() { Kind = DiscountKind.Percentage, Value = value, Scope = scope, ProductId = product, Enabled = true };

    private static readonly IReadOnlyList<BillLine> Lines = new[]
    {
        new BillLine(1, 4.50m, 2),   // 9.00
        new BillLine(2, 12.00m, 1),  // 12.00
        new BillLine(3, 3.25m, 4)    // 13.00
    };

    // ---------- When a discount is on ----------

    [Fact]
    public void An_enabled_discount_without_a_day_or_time_is_always_on() =>
        Assert.True(PosRules.DiscountActive(Pct(10), Friday8pm));

    [Fact]
    public void A_switched_off_discount_never_applies() =>
        Assert.False(PosRules.DiscountActive(new Discount { Kind = DiscountKind.Percentage, Value = 10, Enabled = false }, Friday8pm));

    [Theory]
    [InlineData(DayOfWeek.Friday, true)]
    [InlineData(DayOfWeek.Saturday, false)]
    public void A_discount_for_one_weekday_applies_on_that_day_only(DayOfWeek day, bool active)
    {
        var d = Pct(10);
        d.DayOfWeek = day;
        Assert.Equal(active, PosRules.DiscountActive(d, Friday8pm));
    }

    [Theory]
    [InlineData("17:00", "21:00", true)]
    [InlineData("20:00", "21:00", true)]   // starts now
    [InlineData("17:00", "20:00", false)]  // ended just now
    [InlineData("22:00", "02:00", false)]  // later tonight
    [InlineData("18:00", "02:00", true)]   // over midnight, now inside
    public void A_happy_hour_applies_inside_its_window(string from, string to, bool active)
    {
        var d = Pct(10);
        d.StartsAt = TimeOnly.Parse(from);
        d.EndsAt = TimeOnly.Parse(to);
        Assert.Equal(active, PosRules.DiscountActive(d, Friday8pm));
    }

    [Fact]
    public void An_overnight_happy_hour_still_applies_after_midnight()
    {
        var d = Pct(10);
        d.StartsAt = new TimeOnly(18, 0);
        d.EndsAt = new TimeOnly(2, 0);
        Assert.True(PosRules.DiscountActive(d, new DateTime(2026, 11, 28, 1, 30, 0)));
    }

    // ---------- How much it takes off ----------

    [Fact]
    public void A_percentage_off_the_bill_is_rounded_to_cents() =>
        Assert.Equal(3.40m, PosRules.DiscountAmount(Pct(10), Lines)); // 10 % of 34.00

    [Fact]
    public void A_percentage_off_one_dish_counts_only_that_dish() =>
        Assert.Equal(1.80m, PosRules.DiscountAmount(Pct(20, DiscountScope.Item, product: 1), Lines)); // 20 % of 9.00

    [Fact]
    public void An_amount_off_never_exceeds_what_it_applies_to() =>
        Assert.Equal(12.00m, PosRules.DiscountAmount(
            new Discount { Kind = DiscountKind.FixedAmount, Value = 50, Scope = DiscountScope.Item, ProductId = 2, Enabled = true }, Lines));

    [Fact]
    public void A_discount_for_a_dish_not_on_the_bill_takes_nothing_off() =>
        Assert.Equal(0m, PosRules.DiscountAmount(Pct(50, DiscountScope.Item, product: 99), Lines));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void Impossible_percentages_take_nothing_off(int value) =>
        Assert.Equal(0m, PosRules.DiscountAmount(Pct(value), Lines));

    // ---------- The bill ----------

    [Fact]
    public void The_bill_adds_up_its_lines_and_takes_off_the_discount()
    {
        var bill = PosRules.Summarize(Lines, 3.40m);
        Assert.Equal(new BillSummary(34.00m, 3.40m, 30.60m), bill);
    }

    [Fact]
    public void A_discount_can_never_make_the_bill_negative() =>
        Assert.Equal(new BillSummary(34.00m, 34.00m, 0m), PosRules.Summarize(Lines, 80m));

    // ---------- Paying ----------

    [Fact]
    public void Exact_cash_pays_the_bill_with_no_change() =>
        Assert.Equal(new PaymentCheck(true, 0m, null), PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Cash, 30.60m) }));

    [Fact]
    public void Extra_cash_is_given_back_as_change() =>
        Assert.Equal(new PaymentCheck(true, 9.40m, null), PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Cash, 40m) }));

    [Fact]
    public void Card_and_cash_together_can_pay_one_bill() =>
        Assert.True(PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Card, 20m), new Tender(PaymentMethod.Cash, 20m) }).Ok);

    [Fact]
    public void Change_only_comes_from_cash() =>
        Assert.Equal(9.40m, PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Card, 20m), new Tender(PaymentMethod.Cash, 20m) }).Change);

    [Fact]
    public void Too_little_is_refused() =>
        Assert.False(PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Cash, 30m) }).Ok);

    [Fact]
    public void A_card_cannot_be_charged_more_than_the_bill() =>
        Assert.False(PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Card, 31m) }).Ok);

    [Fact]
    public void Zero_or_negative_amounts_are_refused() =>
        Assert.False(PosRules.CheckPayment(30.60m, new[] { new Tender(PaymentMethod.Cash, 40m), new Tender(PaymentMethod.Card, 0m) }).Ok);

    [Fact]
    public void A_free_bill_needs_no_payment() =>
        Assert.True(PosRules.CheckPayment(0m, Array.Empty<Tender>()).Ok);

    // ---------- Addressing a bill ----------

    [Theory]
    [InlineData("t12", 12, null)]
    [InlineData("o345", null, 345)]
    public void A_bill_is_a_table_or_one_takeaway_order(string key, int? table, int? order) =>
        Assert.Equal((table, order), PosRules.ParseBillKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x12")]
    [InlineData("t")]
    [InlineData("t-3")]
    [InlineData("t12;drop")]
    public void Anything_else_is_not_a_bill(string? key) =>
        Assert.Equal(((int?)null, (int?)null), PosRules.ParseBillKey(key));

    [Fact]
    public void Bill_keys_round_trip()
    {
        Assert.Equal("t7", PosRules.BillKey(7, null));
        Assert.Equal("o9", PosRules.BillKey(null, 9));
    }

    [Theory]
    [InlineData("dinein", OrderType.DineIn)]
    [InlineData("takeaway", OrderType.Takeaway)]
    public void The_screen_names_the_order_type(string value, OrderType type) =>
        Assert.Equal(type, PosRules.ParseOrderType(value));

    [Fact]
    public void An_unknown_order_type_is_refused() => Assert.Null(PosRules.ParseOrderType("delivery"));
}
