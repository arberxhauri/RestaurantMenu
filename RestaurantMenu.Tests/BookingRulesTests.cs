using static RestaurantMenu.Tests.TestClock;

namespace RestaurantMenu.Tests;

public class BookingRulesTests
{
    private static ReservationSettings Settings(int lead = 120, int lastSeating = 60, int step = 30, int covers = 20, string? closed = null) => new()
    {
        Enabled = true,
        SlotMinutes = step,
        CoversPerSlot = covers,
        LeadMinutes = lead,
        MaxDaysAhead = 60,
        LastSeatingMinutes = lastSeating,
        ClosedDates = closed
    };

    private static readonly Dictionary<DateTime, int> NoneBooked = new();
    private static readonly DateOnly Monday = new(2026, 10, 5);

    [Fact]
    public void Slots_run_from_opening_until_last_seating()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 18, 22) };
        var slots = BookingRules.Slots(Settings(), hours, Tirana, Monday, Utc(2026, 10, 4, 12), NoneBooked);

        Assert.Equal(new[] { "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00" }, slots.Select(s => s.Time));
        Assert.All(slots, s => Assert.True(s.Open));
    }

    [Fact]
    public void A_full_slot_is_closed()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 18, 22) };
        var booked = new Dictionary<DateTime, int> { [new DateTime(2026, 10, 5, 19, 0, 0)] = 20 };
        var slots = BookingRules.Slots(Settings(), hours, Tirana, Monday, Utc(2026, 10, 4, 12), booked);

        var seven = slots.Single(s => s.Time == "19:00");
        Assert.False(seven.Open);
        Assert.Equal(0, seven.Remaining);
        Assert.True(slots.Single(s => s.Time == "19:30").Open);
    }

    [Fact]
    public void Slots_inside_the_notice_period_are_closed()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 18, 22) };
        var slots = BookingRules.Slots(Settings(lead: 120), hours, Tirana, Monday, Utc(2026, 10, 5, 17, 30), NoneBooked);

        Assert.Equal(new[] { "19:30", "20:00", "20:30", "21:00" }, slots.Where(s => s.Open).Select(s => s.Time));
    }

    [Fact]
    public void Evening_past_midnight_keeps_late_slots_on_its_own_day()
    {
        var hours = new[] { Hours(DayOfWeek.Friday, 20, 2) };
        var friday = new DateOnly(2026, 10, 9);
        var slots = BookingRules.Slots(Settings(), hours, Tirana, friday, Utc(2026, 10, 4, 12), NoneBooked);

        Assert.Equal("20:00", slots[0].Time);
        Assert.Equal("01:00", slots[^1].Time);
        Assert.Equal(new DateTime(2026, 10, 10, 1, 0, 0), slots[^1].Local);
        Assert.Equal(11, slots.Count);
    }

    [Fact]
    public void Closed_day_has_no_open_slots()
    {
        var hours = new[] { Hours(DayOfWeek.Monday, 18, 22) };
        var slots = BookingRules.Slots(Settings(closed: "2026-10-05"), hours, Tirana, Monday, Utc(2026, 10, 4, 12), NoneBooked);

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.False(s.Open));
    }

    [Fact]
    public void Time_skipped_when_clocks_go_forward_does_not_exist()
    {
        // Tirana springs forward at 02:00 on Sunday 29 March 2026.
        var hours = new[] { Hours(DayOfWeek.Saturday, 22, 4) };
        var slots = BookingRules.Slots(Settings(lastSeating: 0, step: 60), hours, Tirana, new DateOnly(2026, 3, 28), Utc(2026, 3, 20, 12), NoneBooked);

        Assert.Equal(new[] { "22:00", "23:00", "00:00", "01:00", "03:00", "04:00" }, slots.Select(s => s.Time));
        // 01:00 is UTC+1 and 03:00 is UTC+2, so they are one real hour apart.
        Assert.Equal(TimeSpan.FromHours(1), slots[4].Utc - slots[3].Utc);
    }

    [Fact]
    public void Disabled_bookings_have_no_open_slots()
    {
        var s = Settings();
        s.Enabled = false;
        var slots = BookingRules.Slots(s, new[] { Hours(DayOfWeek.Monday, 18, 22) }, Tirana, Monday, Utc(2026, 10, 4, 12), NoneBooked);
        Assert.All(slots, x => Assert.False(x.Open));
    }

    [Theory]
    [InlineData("069 123 4567", "+355691234567")]
    [InlineData("+39 333 1234567", "+393331234567")]
    [InlineData("0039 333 1234567", "+393331234567")]
    [InlineData("355 69 123 4567", "+355691234567")]
    [InlineData("(069) 123-4567", "+355691234567")]
    [InlineData("call me", null)]
    [InlineData("12", null)]
    [InlineData("", null)]
    public void Phones_are_normalised(string input, string? expected) =>
        Assert.Equal(expected, BookingRules.NormalizePhone(input, "355"));

    [Theory]
    [InlineData("+355691234567", "+355 69 123 4567")]
    [InlineData("+393331234567", "+39 333 123 4567")]
    [InlineData("+14155550123", "+1 415 555 0123")]
    public void Phones_are_formatted_for_reading(string stored, string expected) =>
        Assert.Equal(expected, BookingRules.FormatPhone(stored));

    [Fact]
    public void Closed_dates_ignore_junk_and_past_days()
    {
        Assert.Equal(new HashSet<DateOnly> { new(2026, 10, 5) }, BookingRules.ClosedDates("2026-10-05, nonsense,,2026-13-40"));
        Assert.Equal("2026-10-05,2026-10-07",
            BookingRules.StoreClosedDates(new[] { new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7) }, Monday));
    }

    [Fact]
    public void Text_is_cleaned_to_one_line_and_capped()
    {
        Assert.Equal("Birthday cake please", BookingRules.Clean("  Birthday\n\ncake   please ", 300));
        Assert.Equal("abc", BookingRules.Clean("abcdef", 3));
        Assert.Null(BookingRules.Clean("   ", 10));
    }

    [Theory]
    [InlineData("guest@example.al", true)]
    [InlineData("guest@example", false)]
    [InlineData("not an email", false)]
    [InlineData(null, false)]
    public void Emails(string? email, bool ok) => Assert.Equal(ok, BookingRules.IsEmail(email));

    [Theory]
    [InlineData("en", "Oliva uses your details only for this booking.", "Privacy")]
    [InlineData("sq", "Oliva i përdor të dhënat tuaja vetëm për këtë rezervim.", "Privatësia")]
    [InlineData("xx", "Oliva uses your details only for this booking.", "Privacy")]
    public void Privacy_note_is_translated(string lang, string text, string link)
    {
        var note = BookingText.PrivacyNote("Oliva", lang);
        Assert.Equal(text, note.Text);
        Assert.Equal(link, note.Link);
    }

    [Fact]
    public void Privacy_note_exists_in_every_menu_language()
    {
        var english = BookingText.PrivacyNote("X", "en");
        foreach (var lang in new[] { "sq", "it", "de", "fr", "es", "tr" })
        {
            var note = BookingText.PrivacyNote("X", lang);
            Assert.Contains("X", note.Text);
            Assert.NotEqual(english.Text, note.Text);
        }
    }
}
