namespace RestaurantMenu.Tests;

/// <summary>Reports: which days a report covers, the period it compares with, the change, and safe CSV.</summary>
public class ReportRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    [Fact]
    public void Without_dates_a_report_covers_the_last_seven_days() =>
        Assert.Equal((new DateOnly(2026, 11, 18), Today), ReportRules.Period(null, null, Today));

    [Fact]
    public void Chosen_dates_are_used() =>
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10)), ReportRules.Period("2026-11-01", "2026-11-10", Today));

    [Fact]
    public void A_future_end_stops_at_today() =>
        Assert.Equal((new DateOnly(2026, 11, 20), Today), ReportRules.Period("2026-11-20", "2026-12-31", Today));

    [Fact]
    public void Swapped_dates_are_put_in_order() =>
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10)), ReportRules.Period("2026-11-10", "2026-11-01", Today));

    [Fact]
    public void A_report_covers_at_most_a_year() =>
        Assert.Equal((new DateOnly(2025, 11, 24), Today), ReportRules.Period("2020-01-01", "2026-11-24", Today));

    [Fact]
    public void Unreadable_dates_fall_back_to_the_last_seven_days() =>
        Assert.Equal((new DateOnly(2026, 11, 18), Today), ReportRules.Period("yesterday", "x", Today));

    [Fact]
    public void The_previous_period_has_the_same_length_and_ends_the_day_before() =>
        Assert.Equal((new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 17)),
            ReportRules.Previous(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24)));

    [Theory]
    [InlineData(120, 100, 20.0)]
    [InlineData(50, 200, -75.0)]
    [InlineData(100, 300, -66.7)]
    public void The_change_is_a_percentage_of_the_previous_period(double current, double previous, double change) =>
        Assert.Equal((decimal)change, ReportRules.Change((decimal)current, (decimal)previous));

    [Fact]
    public void There_is_no_change_to_show_after_an_empty_period() => Assert.Null(ReportRules.Change(50m, 0m));

    [Fact]
    public void Csv_quotes_only_what_needs_it() =>
        Assert.Equal("Dish,Qty\r\n\"Fish, grilled\",2\r\n\"Say \"\"hi\"\"\",1\r\n",
            ReportRules.Csv(new[] { new[] { "Dish", "Qty" }, new[] { "Fish, grilled", "2" }, new[] { "Say \"hi\"", "1" } }));

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("-2")]
    [InlineData("@cmd")]
    public void Csv_never_lets_a_cell_run_as_a_spreadsheet_formula(string cell) =>
        Assert.StartsWith("'", ReportRules.Csv(new[] { new[] { cell } }));
}
