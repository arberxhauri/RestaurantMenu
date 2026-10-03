using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Opening hours: whether a branch is open at a moment, the guest-facing label for it
/// ("Open until 23:00", "Closed · opens tomorrow 08:00"), and the branch form's parsing
/// and validation. Pure functions of the hours, the zone and the time, so they are easy
/// to test.
/// </summary>
public static class OpeningHours
{
    public const string DefaultTimeZone = "Europe/Tirane";

    /// <summary>The week as guests read it in Europe: Monday first.</summary>
    public static readonly DayOfWeek[] Week =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    /// <summary>Time zones offered in the branch form (IANA ids), Albania first.</summary>
    public static readonly (string Id, string Label)[] TimeZones =
    {
        ("Europe/Tirane", "Albania (Tirana)"),
        ("Europe/Belgrade", "Kosovo, Serbia (Prishtina, Belgrade)"),
        ("Europe/Skopje", "North Macedonia (Skopje)"),
        ("Europe/Podgorica", "Montenegro (Podgorica)"),
        ("Europe/Athens", "Greece (Athens)"),
        ("Europe/Rome", "Italy (Rome)"),
        ("Europe/Vienna", "Austria (Vienna)"),
        ("Europe/Berlin", "Germany (Berlin)"),
        ("Europe/Zurich", "Switzerland (Zurich)"),
        ("Europe/Paris", "France (Paris)"),
        ("Europe/Brussels", "Belgium (Brussels)"),
        ("Europe/Amsterdam", "Netherlands (Amsterdam)"),
        ("Europe/Madrid", "Spain (Madrid)"),
        ("Europe/London", "United Kingdom (London)"),
        ("Europe/Istanbul", "Türkiye (Istanbul)")
    };

    public static bool IsKnownTimeZone(string? id) => id != null && TimeZones.Any(z => z.Id == id);

    /// <summary>The zone for an id; Tirana if the id is unknown on this machine.</summary>
    public static TimeZoneInfo Zone(string? id)
    {
        foreach (var candidate in new[] { id, DefaultTimeZone })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    // ---------------------------------------------------------------- status

    /// <param name="Until">While open: when this stretch of opening ends (local time).</param>
    /// <param name="AllDay">Open for more than the next 24 hours without a break.</param>
    /// <param name="NextOpen">While closed: the next opening (local time), or null if never.</param>
    public record Status(bool IsOpen, bool AllDay, DateTime? Until, DateTime? NextOpen, DateTime LocalNow);

    public static Status GetStatus(IEnumerable<BranchHours> hours, TimeZoneInfo zone, DateTime utcNow)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var periods = hours.ToList();

        // Concrete periods from yesterday (an overnight period may still be running) to
        // eight days ahead, merged where one ends exactly when the next starts, so
        // 18:00–24:00 followed by 00:00–02:00 reads as open until 02:00.
        var spans = new List<(DateTime Start, DateTime End)>();
        for (var d = -1; d <= 8; d++)
        {
            var date = now.Date.AddDays(d);
            foreach (var p in periods.Where(p => p.DayOfWeek == date.DayOfWeek))
            {
                var start = date + p.Opens.ToTimeSpan();
                var end = p.Closes > p.Opens ? date + p.Closes.ToTimeSpan() : date.AddDays(1) + p.Closes.ToTimeSpan();
                spans.Add((start, end));
            }
        }

        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var s in spans.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && s.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, s.End > last.End ? s.End : last.End);
            }
            else
            {
                merged.Add(s);
            }
        }

        foreach (var s in merged)
        {
            if (s.Start <= now && now < s.End)
            {
                return new Status(true, s.End - now > TimeSpan.FromHours(24), s.End, null, now);
            }
        }

        var next = merged.Where(s => s.Start > now).Select(s => (DateTime?)s.Start).FirstOrDefault();
        return new Status(false, false, null, next, now);
    }

    // ---------------------------------------------------------------- guest text

    private record Texts(string OpenUntil, string Open24, string OpensAt, string OpensTomorrow, string OpensOn,
        string Closed, string Title, string Today);

    // {0} = time, {1} = weekday name in that language.
    private static readonly Dictionary<string, Texts> Words = new()
    {
        ["en"] = new("Open until {0}", "Open 24 hours", "Closed · opens {0}", "Closed · opens tomorrow {0}", "Closed · opens {1} {0}", "Closed", "Opening hours", "Today"),
        ["sq"] = new("Hapur deri në {0}", "Hapur 24 orë", "Mbyllur · hapet në {0}", "Mbyllur · hapet nesër në {0}", "Mbyllur · hapet {1} në {0}", "Mbyllur", "Orari", "Sot"),
        ["it"] = new("Aperto fino alle {0}", "Aperto 24 ore", "Chiuso · apre alle {0}", "Chiuso · apre domani alle {0}", "Chiuso · apre {1} alle {0}", "Chiuso", "Orari", "Oggi"),
        ["de"] = new("Geöffnet bis {0}", "24 Stunden geöffnet", "Geschlossen · öffnet um {0}", "Geschlossen · öffnet morgen um {0}", "Geschlossen · öffnet {1} um {0}", "Geschlossen", "Öffnungszeiten", "Heute"),
        ["fr"] = new("Ouvert jusqu'à {0}", "Ouvert 24 h/24", "Fermé · ouvre à {0}", "Fermé · ouvre demain à {0}", "Fermé · ouvre {1} à {0}", "Fermé", "Horaires", "Aujourd'hui"),
        ["es"] = new("Abierto hasta las {0}", "Abierto 24 horas", "Cerrado · abre a las {0}", "Cerrado · abre mañana a las {0}", "Cerrado · abre el {1} a las {0}", "Cerrado", "Horario", "Hoy"),
        ["tr"] = new("{0} saatine kadar açık", "24 saat açık", "Kapalı · {0} saatinde açılıyor", "Kapalı · yarın {0} saatinde açılıyor", "Kapalı · {1} {0} saatinde açılıyor", "Kapalı", "Çalışma saatleri", "Bugün")
    };

    private static Texts For(string? language) => Words.TryGetValue(language ?? "en", out var t) ? t : Words["en"];

    public static string Time(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string Time(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Weekday name in the menu language ("Monday", "e hënë", "lunedì"…).</summary>
    public static string DayName(DayOfWeek day, string? language)
    {
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(language ?? "en"); }
        catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo("en"); }
        return culture.DateTimeFormat.GetDayName(day);
    }

    /// <summary>"Open until 23:00", "Open 24 hours", "Closed · opens tomorrow 08:00", "Closed".</summary>
    public static string Label(Status status, string? language)
    {
        var t = For(language);
        if (status.IsOpen)
        {
            return status.AllDay ? t.Open24 : string.Format(t.OpenUntil, Time(status.Until!.Value));
        }
        if (status.NextOpen is not { } next)
        {
            return t.Closed;
        }
        var days = (next.Date - status.LocalNow.Date).Days;
        return days switch
        {
            0 => string.Format(t.OpensAt, Time(next)),
            1 => string.Format(t.OpensTomorrow, Time(next)),
            _ => string.Format(t.OpensOn, Time(next), DayName(next.DayOfWeek, language))
        };
    }

    public static string Title(string? language) => For(language).Title;
    public static string Today(string? language) => For(language).Today;

    /// <summary>One weekday for the menu's hours list: "12:00–15:00, 19:00–23:00", "Open 24 hours" or "Closed".</summary>
    public static string DayText(IEnumerable<BranchHours> hours, DayOfWeek day, string? language)
    {
        var t = For(language);
        var periods = hours.Where(h => h.DayOfWeek == day).OrderBy(h => h.Opens).ToList();
        if (periods.Count == 0) return t.Closed;
        if (periods.Count == 1 && periods[0].Opens == periods[0].Closes) return t.Open24;
        return string.Join(", ", periods.Select(p => $"{Time(p.Opens)}–{Time(p.Closes)}"));
    }

    // ---------------------------------------------------------------- branch form

    /// <summary>One weekday row in the branch form, as typed (strings, so invalid input can be shown back).</summary>
    public class FormDay
    {
        public DayOfWeek Day { get; set; }
        public bool Closed { get; set; }
        public string Open1 { get; set; } = "";
        public string Close1 { get; set; } = "";
        public string Open2 { get; set; } = "";
        public string Close2 { get; set; } = "";
        public bool HasSecond => Open2.Length > 0 || Close2.Length > 0;
    }

    public static string FieldName(DayOfWeek day, string part) => $"hours_{day.ToString().ToLowerInvariant()}_{part}";

    /// <summary>The form rows for saved hours. With none saved, every day 09:00–22:00 as a starting point.</summary>
    public static List<FormDay> ToForm(IEnumerable<BranchHours>? saved)
    {
        var hours = saved?.ToList() ?? new List<BranchHours>();
        return Week.Select(day =>
        {
            if (hours.Count == 0)
            {
                return new FormDay { Day = day, Open1 = "09:00", Close1 = "22:00" };
            }
            var periods = hours.Where(h => h.DayOfWeek == day).OrderBy(h => h.Opens).ToList();
            return new FormDay
            {
                Day = day,
                Closed = periods.Count == 0,
                Open1 = periods.Count > 0 ? Time(periods[0].Opens) : "",
                Close1 = periods.Count > 0 ? Time(periods[0].Closes) : "",
                Open2 = periods.Count > 1 ? Time(periods[1].Opens) : "",
                Close2 = periods.Count > 1 ? Time(periods[1].Closes) : ""
            };
        }).ToList();
    }

    public static List<FormDay> FromForm(IFormCollection form) => Week.Select(day => new FormDay
    {
        Day = day,
        Closed = form[FieldName(day, "closed")].ToString() == "true",
        Open1 = form[FieldName(day, "open1")].ToString().Trim(),
        Close1 = form[FieldName(day, "close1")].ToString().Trim(),
        Open2 = form[FieldName(day, "open2")].ToString().Trim(),
        Close2 = form[FieldName(day, "close2")].ToString().Trim()
    }).ToList();

    private static bool TryTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>
    /// Turns the form rows into periods. Errors are written for the owner, one per
    /// problem; days with an error contribute no periods.
    /// </summary>
    public static (List<BranchHours> Periods, List<string> Errors) Validate(IReadOnlyList<FormDay> days)
    {
        var periods = new List<BranchHours>();
        var errors = new List<string>();

        foreach (var d in days)
        {
            if (d.Closed) continue;
            var name = d.Day.ToString();

            if (!TryTime(d.Open1, out var o1) || !TryTime(d.Close1, out var c1))
            {
                errors.Add($"{name}: enter an opening and a closing time, or tick Closed.");
                continue;
            }

            var day = new List<BranchHours> { new() { DayOfWeek = d.Day, Opens = o1, Closes = c1 } };
            if (d.HasSecond)
            {
                if (!TryTime(d.Open2, out var o2) || !TryTime(d.Close2, out var c2))
                {
                    errors.Add($"{name}: enter both times of the second period, or clear them.");
                    continue;
                }
                if (c1 <= o1)
                {
                    errors.Add($"{name}: a day that runs past midnight or is open 24 hours can only have one period.");
                    continue;
                }
                if (o2 < c1)
                {
                    errors.Add($"{name}: the second period must start after the first one ends ({Time(c1)}).");
                    continue;
                }
                day.Add(new BranchHours { DayOfWeek = d.Day, Opens = o2, Closes = c2 });
            }
            periods.AddRange(day);
        }

        // A period running past midnight must end before the next day opens.
        foreach (var late in periods.Where(p => p.Closes < p.Opens))
        {
            var nextDay = (DayOfWeek)(((int)late.DayOfWeek + 1) % 7);
            var firstNext = periods.Where(p => p.DayOfWeek == nextDay).OrderBy(p => p.Opens).FirstOrDefault();
            if (firstNext != null && firstNext.Opens < late.Closes)
            {
                errors.Add($"{late.DayOfWeek}: open until {Time(late.Closes)}, but {nextDay} opens at {Time(firstNext.Opens)}. Adjust one of them.");
            }
        }

        return (errors.Count == 0 ? periods : periods.Where(p => !errors.Any(e => e.StartsWith(p.DayOfWeek + ":"))).ToList(), errors);
    }
}
