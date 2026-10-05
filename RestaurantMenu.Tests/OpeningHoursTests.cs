using static RestaurantMenu.Tests.TestClock;

namespace RestaurantMenu.Tests;

// 2026-10-05 is a Monday. Tirana is UTC+2 until 25 October 2026, then UTC+1.
public class OpeningHoursTests
{
    [Fact]
    public void Open_inside_a_period()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 12, 22) };
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 5, 14));

        Assert.True(s.IsOpen);
        Assert.False(s.AllDay);
        Assert.Equal(new DateTime(2026, 10, 5, 22, 0, 0), s.Until);
        Assert.Equal("Open until 22:00", OpeningHours.Label(s, "en"));
    }

    [Fact]
    public void Overnight_period_is_still_open_after_midnight()
    {
        // Friday 18:00–02:00, checked at Saturday 01:00.
        var hours = new[] { Hours(DayOfWeek.Friday, 18, 2) };
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 10, 1));

        Assert.True(s.IsOpen);
        Assert.Equal(new DateTime(2026, 10, 10, 2, 0, 0), s.Until);
    }

    [Fact]
    public void Periods_that_touch_at_midnight_are_merged()
    {
        // Monday 18:00–24:00 then Tuesday 00:00–02:00 reads "open until 02:00".
        var hours = new[] { Hours(DayOfWeek.Monday, 18, 0), Hours(DayOfWeek.Tuesday, 0, 2) };
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 5, 23));

        Assert.True(s.IsOpen);
        Assert.Equal(new DateTime(2026, 10, 6, 2, 0, 0), s.Until);
    }

    [Fact]
    public void Equal_times_every_day_is_open_24_hours()
    {
        var hours = OpeningHours.Week.Select(d => Hours(d, 0, 0)).ToList();
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 7, 3));

        Assert.True(s.IsOpen);
        Assert.True(s.AllDay);
        Assert.Equal("Open 24 hours", OpeningHours.Label(s, "en"));
    }

    [Fact]
    public void Closed_label_names_tomorrow()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 12, 22), Hours(DayOfWeek.Tuesday, 9, 17) };
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 5, 23));

        Assert.False(s.IsOpen);
        Assert.Equal(new DateTime(2026, 10, 6, 9, 0, 0), s.NextOpen);
        Assert.Equal("Closed · opens tomorrow 09:00", OpeningHours.Label(s, "en"));
    }

    [Fact]
    public void Closed_label_wraps_round_the_week()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 12, 22) };
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 6, 10));

        Assert.Equal(new DateTime(2026, 10, 12, 12, 0, 0), s.NextOpen);
        Assert.Equal("Closed · opens Monday 12:00", OpeningHours.Label(s, "en"));
    }

    [Fact]
    public void Same_day_later_opening()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 19, 23) };
        var s = OpeningHours.GetStatus(hours, Tirana, Utc(2026, 10, 5, 10));

        Assert.Equal("Closed · opens 19:00", OpeningHours.Label(s, "en"));
        Assert.Equal("Mbyllur · hapet në 19:00", OpeningHours.Label(s, "sq"));
    }

    [Fact]
    public void No_hours_is_closed()
    {
        var s = OpeningHours.GetStatus(Array.Empty<BranchHours>(), Tirana, Utc(2026, 10, 5, 12));

        Assert.False(s.IsOpen);
        Assert.Null(s.NextOpen);
        Assert.Equal("Closed", OpeningHours.Label(s, "en"));
    }

    [Fact]
    public void Local_time_follows_daylight_saving()
    {
        // 11:00 UTC is 13:00 in summer time and 12:00 after the clocks go back on 25 October.
        Assert.Equal(13, OpeningHours.GetStatus(Array.Empty<BranchHours>(), Tirana, new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc)).LocalNow.Hour);
        Assert.Equal(12, OpeningHours.GetStatus(Array.Empty<BranchHours>(), Tirana, new DateTime(2026, 10, 26, 11, 0, 0, DateTimeKind.Utc)).LocalNow.Hour);
    }

    [Fact]
    public void Day_text_lists_split_periods_in_order()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 19, 23), Hours(DayOfWeek.Monday, 12, 15) };

        Assert.Equal("12:00–15:00, 19:00–23:00", OpeningHours.DayText(hours, DayOfWeek.Monday, "en"));
        Assert.Equal("Closed", OpeningHours.DayText(hours, DayOfWeek.Tuesday, "en"));
    }

    [Fact]
    public void Unknown_zone_falls_back_to_tirana()
    {
        Assert.Equal(Tirana.BaseUtcOffset, OpeningHours.Zone("Not/AZone").BaseUtcOffset);
    }
}

public class ServingTimesTests
{
    private static Category Cat(AvailableDays days, int? from = null, int? to = null) => new()
    {
        Name = "Breakfast",
        AvailableDays = days,
        AvailableFrom = from is { } f ? new TimeOnly(f, 0) : null,
        AvailableTo = to is { } t ? new TimeOnly(t, 0) : null
    };

    [Theory]
    [InlineData(AvailableDays.Monday | AvailableDays.Tuesday | AvailableDays.Wednesday | AvailableDays.Thursday | AvailableDays.Friday, "Mon–Fri")]
    [InlineData(AvailableDays.Monday | AvailableDays.Wednesday | AvailableDays.Friday, "Mon, Wed, Fri")]
    [InlineData(AvailableDays.Saturday | AvailableDays.Sunday, "Sat, Sun")]
    [InlineData(AvailableDays.All, "")]
    [InlineData(AvailableDays.None, "")]
    public void Day_ranges(AvailableDays days, string expected) =>
        Assert.Equal(expected, ServingTimes.DayRange(days, "en"));

    [Fact]
    public void Schedule_joins_days_and_times()
    {
        var c = Cat(AvailableDays.Monday | AvailableDays.Tuesday | AvailableDays.Wednesday | AvailableDays.Thursday | AvailableDays.Friday, 8, 11);
        Assert.Equal("Mon–Fri · 08:00–11:00", ServingTimes.Schedule(c, "en"));
    }

    [Fact]
    public void Unrestricted_category_has_no_status()
    {
        Assert.Null(ServingTimes.Status(Cat(AvailableDays.None), Tirana, Utc(2026, 10, 5, 12)));
    }

    [Fact]
    public void Breakfast_after_its_window_is_available_tomorrow()
    {
        var s = ServingTimes.Status(Cat(AvailableDays.None, 8, 11), Tirana, Utc(2026, 10, 5, 12))!;

        Assert.False(s.IsOpen);
        Assert.Equal("Available tomorrow from 08:00", ServingTimes.NextNote(s, "en"));
    }

    [Fact]
    public void Window_past_midnight_is_served_at_one_am()
    {
        var s = ServingTimes.Status(Cat(AvailableDays.None, 22, 2), Tirana, Utc(2026, 10, 6, 1))!;
        Assert.True(s.IsOpen);
    }

    [Fact]
    public void Weekend_only_category_on_a_weekday_names_the_day()
    {
        var c = Cat(AvailableDays.Saturday | AvailableDays.Sunday);
        var s = ServingTimes.Status(c, Tirana, Utc(2026, 10, 7, 12))!; // Wednesday

        Assert.False(s.IsOpen);
        Assert.Equal("Available Saturday", ServingTimes.NextNote(s, "en", allDay: true));
    }
}
