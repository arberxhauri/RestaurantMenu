using System.Globalization;
using System.Text.RegularExpressions;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>A bookable time on one day. <see cref="Remaining"/> is guests still bookable in it.</summary>
public record BookingSlot(DateTime Local, DateTime Utc, int Booked, int Remaining, bool Open)
{
    public string Time => Local.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// Booking availability and input rules, with no database (unit-tested). Slots come
/// from the branch's opening hours: every SlotMinutes from opening until LastSeatingMinutes
/// before closing. A slot is open when it is far enough ahead (LeadMinutes), within
/// MaxDaysAhead, the day isn't closed for bookings, and fewer than CoversPerSlot guests are
/// already booked to arrive then.
/// </summary>
public static class BookingRules
{
    public static readonly int[] SlotLengths = { 15, 30, 60 };
    public const int MaxNameLength = 80;
    public const int MaxNoteLength = 300;
    /// <summary>Active (future, not cancelled) online bookings one phone may hold at a branch.</summary>
    public const int MaxActivePerPhone = 3;

    /// <summary>Statuses that hold seats.</summary>
    public static bool HoldsSeats(ReservationStatus s) => s is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated;

    public static HashSet<DateOnly> ClosedDates(string? stored) =>
        (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) ? (DateOnly?)x : null)
            .Where(d => d != null).Select(d => d!.Value).ToHashSet();

    public static string StoreClosedDates(IEnumerable<DateOnly> dates, DateOnly today) =>
        string.Join(",", dates.Where(d => d >= today).Distinct().OrderBy(d => d).Take(300).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    /// <summary>The branch's local date today.</summary>
    public static DateOnly Today(TimeZoneInfo zone, DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone));

    /// <summary>
    /// Every slot starting on <paramref name="date"/> (an evening that runs past midnight
    /// keeps its late slots on the day it started). <paramref name="booked"/> maps a slot's
    /// local start to the guests already holding seats in it.
    /// </summary>
    public static List<BookingSlot> Slots(ReservationSettings s, IEnumerable<BranchHours> hours, TimeZoneInfo zone,
        DateOnly date, DateTime utcNow, IReadOnlyDictionary<DateTime, int> booked)
    {
        var slots = new List<BookingSlot>();
        var step = SlotLengths.Contains(s.SlotMinutes) ? s.SlotMinutes : 30;
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var earliestUtc = utcNow.AddMinutes(Math.Max(0, s.LeadMinutes));
        var today = Today(zone, utcNow);
        var dayOpen = s.Enabled && date >= today && date <= today.AddDays(Math.Max(0, s.MaxDaysAhead)) && !ClosedDates(s.ClosedDates).Contains(date);

        var seen = new HashSet<DateTime>();
        foreach (var p in hours.Where(h => h.DayOfWeek == date.DayOfWeek).OrderBy(h => h.Opens))
        {
            var open = dayStart + p.Opens.ToTimeSpan();
            var close = p.Closes > p.Opens ? dayStart + p.Closes.ToTimeSpan() : dayStart.AddDays(1) + p.Closes.ToTimeSpan();
            var last = close.AddMinutes(-Math.Max(0, s.LastSeatingMinutes));
            for (var t = open; t <= last; t = t.AddMinutes(step))
            {
                // A wall-clock time skipped by the clocks going forward doesn't exist.
                if (!seen.Add(t) || zone.IsInvalidTime(t)) continue;
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone);
                var taken = booked.TryGetValue(t, out var n) ? n : 0;
                var remaining = Math.Max(0, s.CoversPerSlot - taken);
                slots.Add(new BookingSlot(t, utc, taken, remaining, dayOpen && utc >= earliestUtc && remaining > 0));
            }
        }
        return slots.OrderBy(x => x.Local).ToList();
    }

    /// <summary>
    /// A phone number in international format (+ and 8 to 15 digits), or null if it can't be one.
    /// "069 123 4567" with country code 355 becomes +355691234567; "00 39 …" becomes +39…
    /// </summary>
    public static string? NormalizePhone(string? input, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var raw = input.Trim();
        if (Regex.IsMatch(raw, @"[^\d\s+\-().\/]")) return null; // letters and the like
        var plus = raw.StartsWith('+');
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        var cc = new string((countryCode ?? "").Where(char.IsAsciiDigit).ToArray());
        if (!plus)
        {
            if (digits.StartsWith("00")) digits = digits[2..];
            else if (digits.StartsWith('0')) digits = digits.Length >= 8 ? cc + digits[1..] : ""; // national numbers have 7+ digits after the 0
            else if (cc.Length > 0 && !digits.StartsWith(cc)) digits = digits.Length >= 7 ? cc + digits : "";
        }
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? "+" + digits : null;
    }

    /// <summary>
    /// "+355 69 123 4567" for reading and dialling; the stored value stays plain. The country
    /// code's length follows the ITU numbering plan (1 and 7 alone; e.g. 39 two digits, 355 three).
    /// </summary>
    public static string FormatPhone(string phone)
    {
        if (!phone.StartsWith('+') || phone.Length < 9) return phone;
        var d = phone[1..];
        var cc = CountryCodeLength(d);
        var national = d[cc..];
        // Last 7 digits as "123 4567", anything before them as its own group ("69").
        var tail = national.Length > 7 ? $"{national[..^7]} {national[^7..^4]} {national[^4..]}"
            : national.Length > 4 ? $"{national[..^4]} {national[^4..]}" : national;
        return $"+{d[..cc]} {tail}";
    }

    private static int CountryCodeLength(string d)
    {
        if (d[0] is '1' or '7') return 1;
        var two = int.Parse(d[..2], CultureInfo.InvariantCulture);
        int[] threeDigitPrefixes = { 21, 22, 23, 24, 25, 26, 29, 35, 37, 38, 42, 50, 59, 67, 68, 69, 80, 85, 87, 88, 96, 97, 99 };
        return threeDigitPrefixes.Contains(two) ? 3 : 2;
    }

    public static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
        return clean.Length == 0 ? null : clean.Length > max ? clean[..max] : clean;
    }

    public static bool IsEmail(string? email) =>
        email != null && email.Length <= 256 && Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    /// <summary>An .ics file so the guest can add the booking to their calendar (2 hours long).</summary>
    public static string Ics(Reservation r, string branchName, string address, string url)
    {
        static string E(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
        string T(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//My Quick Menu//Bookings//EN", "METHOD:PUBLISH", "BEGIN:VEVENT",
            $"UID:{r.PublicId}@myquickmenu", $"DTSTAMP:{T(DateTime.UtcNow)}",
            $"DTSTART:{T(r.StartsAtUtc)}", $"DTEND:{T(r.StartsAtUtc.AddHours(2))}",
            $"SUMMARY:{E($"{branchName} ({r.Guests})")}", $"LOCATION:{E(address)}", $"URL:{url}",
            $"DESCRIPTION:{E(url)}", "END:VEVENT", "END:VCALENDAR", ""
        });
    }
}
