using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Time-based menu categories (breakfast, lunch, happy hour). A category's window is turned
/// into BranchHours-style periods so OpeningHours.GetStatus does the time arithmetic
/// (past midnight, week wrap, time zone) in one tested place.
/// </summary>
public static class ServingTimes
{
    public static bool IsRestricted(Category c) => c.AvailableDays != AvailableDays.None || c.AvailableFrom != null;

    private static readonly (DayOfWeek Day, AvailableDays Flag)[] DayFlags =
    {
        (DayOfWeek.Monday, AvailableDays.Monday), (DayOfWeek.Tuesday, AvailableDays.Tuesday),
        (DayOfWeek.Wednesday, AvailableDays.Wednesday), (DayOfWeek.Thursday, AvailableDays.Thursday),
        (DayOfWeek.Friday, AvailableDays.Friday), (DayOfWeek.Saturday, AvailableDays.Saturday),
        (DayOfWeek.Sunday, AvailableDays.Sunday)
    };

    /// <summary>The weekdays served, Monday first; all seven when no day is set.</summary>
    public static List<DayOfWeek> Days(AvailableDays days) =>
        DayFlags.Where(d => days == AvailableDays.None || days.HasFlag(d.Flag)).Select(d => d.Day).ToList();

    /// <summary>The window as periods: From–To on each served day, or the whole day when no times are set.</summary>
    public static List<BranchHours> Periods(Category c)
    {
        var from = c.AvailableFrom ?? TimeOnly.MinValue;
        var to = c.AvailableTo ?? TimeOnly.MinValue; // equal times = the whole day
        return Days(c.AvailableDays).Select(d => new BranchHours { DayOfWeek = d, Opens = from, Closes = to }).ToList();
    }

    /// <summary>Null when the category is always served; otherwise whether it is served now and when next.</summary>
    public static OpeningHours.Status? Status(Category c, TimeZoneInfo zone, DateTime utcNow) =>
        IsRestricted(c) ? OpeningHours.GetStatus(Periods(c), zone, utcNow) : null;

    // ---------------------------------------------------------------- wording

    private record Texts(string Served, string From, string Tomorrow, string OnDay, string NotAvailable, string NothingNow,
        string TomorrowAllDay, string OnDayAllDay);

    // Served: {0} = schedule. From/Tomorrow/OnDay: {0} = time, {1} = weekday. *AllDay: categories with days but no times.
    private static readonly Dictionary<string, Texts> Words = new()
    {
        ["en"] = new("Served {0}", "Available from {0}", "Available tomorrow from {0}", "Available {1} from {0}", "Not available", "Nothing is being served right now", "Available tomorrow", "Available {1}"),
        ["sq"] = new("Shërbehet {0}", "E disponueshme nga ora {0}", "E disponueshme nesër nga ora {0}", "E disponueshme: {1}, nga ora {0}", "Nuk ofrohet", "Asgjë nuk shërbehet tani", "E disponueshme nesër", "E disponueshme: {1}"),
        ["it"] = new("Servito {0}", "Disponibile dalle {0}", "Disponibile domani dalle {0}", "Disponibile {1} dalle {0}", "Non disponibile", "Al momento non si serve nulla", "Disponibile domani", "Disponibile {1}"),
        ["de"] = new("Erhältlich {0}", "Erhältlich ab {0}", "Erhältlich morgen ab {0}", "Erhältlich {1} ab {0}", "Nicht erhältlich", "Gerade wird nichts serviert", "Erhältlich morgen", "Erhältlich am {1}"),
        ["fr"] = new("Servi {0}", "Disponible à partir de {0}", "Disponible demain à partir de {0}", "Disponible {1} à partir de {0}", "Indisponible", "Rien n'est servi pour le moment", "Disponible demain", "Disponible {1}"),
        ["es"] = new("Disponible {0}", "Disponible desde las {0}", "Disponible mañana desde las {0}", "Disponible el {1} desde las {0}", "No disponible", "Ahora mismo no se sirve nada", "Disponible mañana", "Disponible el {1}"),
        ["tr"] = new("Servis: {0}", "{0} itibarıyla serviste", "Yarın {0} itibarıyla serviste", "{1} {0} itibarıyla serviste", "Şu an yok", "Şu anda servis edilen bir şey yok", "Yarın serviste", "{1} serviste")
    };

    private static Texts For(string? language) => Words.TryGetValue(language ?? "en", out var t) ? t : Words["en"];

    private static CultureInfo Culture(string? language)
    {
        try { return CultureInfo.GetCultureInfo(language ?? "en"); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
    }

    /// <summary>"Mon–Fri", "Sat, Sun", "Mon, Wed, Fri" in the menu language; empty for every day.</summary>
    public static string DayRange(AvailableDays days, string? language)
    {
        var served = Days(days);
        if (served.Count == 7) return "";
        var culture = Culture(language);
        string Abbr(DayOfWeek d) => culture.DateTimeFormat.GetAbbreviatedDayName(d).TrimEnd('.');

        // Runs of consecutive days (Monday first): three or more become "Mon–Fri".
        var week = OpeningHours.Week.ToList();
        var parts = new List<string>();
        var run = new List<DayOfWeek>();
        void Flush()
        {
            if (run.Count >= 3) parts.Add($"{Abbr(run[0])}–{Abbr(run[^1])}");
            else parts.AddRange(run.Select(Abbr));
            run.Clear();
        }
        foreach (var d in week)
        {
            if (served.Contains(d)) run.Add(d); else Flush();
        }
        Flush();
        return string.Join(", ", parts);
    }

    /// <summary>The schedule alone: "Mon–Fri · 08:00–11:30", "08:00–11:30", "Sat, Sun".</summary>
    public static string Schedule(Category c, string? language)
    {
        var days = DayRange(c.AvailableDays, language);
        var times = c.AvailableFrom is { } f && c.AvailableTo is { } t ? $"{OpeningHours.Time(f)}–{OpeningHours.Time(t)}" : "";
        return string.Join(" · ", new[] { days, times }.Where(x => x.Length > 0));
    }

    /// <summary>Note under the category title: "Served 08:00–11:30".</summary>
    public static string ServedNote(Category c, string? language) => string.Format(For(language).Served, Schedule(c, language));

    /// <summary>While not served: "Available from 17:00", "… tomorrow from 08:00", "… Friday from 08:00".</summary>
    /// <param name="allDay">The category has days but no times, so "from 00:00" is left out.</param>
    public static string NextNote(OpeningHours.Status status, string? language, bool allDay = false)
    {
        var t = For(language);
        if (status.NextOpen is not { } next) return t.NotAvailable;
        var time = OpeningHours.Time(next);
        var days = (next.Date - status.LocalNow.Date).Days;
        if (allDay && days >= 1)
        {
            return days == 1 ? t.TomorrowAllDay : string.Format(t.OnDayAllDay, time, OpeningHours.DayName(next.DayOfWeek, language));
        }
        return days switch
        {
            0 => string.Format(t.From, time),
            1 => string.Format(t.Tomorrow, time),
            _ => string.Format(t.OnDay, time, OpeningHours.DayName(next.DayOfWeek, language))
        };
    }

    public static string NothingNow(string? language) => For(language).NothingNow;

    // ---------------------------------------------------------------- category form

    public class Form
    {
        public bool Enabled { get; set; }
        public HashSet<DayOfWeek> Days { get; set; } = new(OpeningHours.Week);
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public bool Hide { get; set; }
    }

    public static Form ToForm(Category? c) => c == null || !IsRestricted(c)
        ? new Form { Hide = c?.HideWhenUnavailable ?? false }
        : new Form
        {
            Enabled = true,
            Days = Days(c.AvailableDays).ToHashSet(),
            From = c.AvailableFrom is { } f ? OpeningHours.Time(f) : "",
            To = c.AvailableTo is { } t ? OpeningHours.Time(t) : "",
            Hide = c.HideWhenUnavailable
        };

    public static Form FromForm(IFormCollection form) => new()
    {
        Enabled = form["schedule_enabled"].ToString() == "true",
        Days = form["schedule_days"].Select(v => Enum.TryParse<DayOfWeek>(v, out var d) ? (DayOfWeek?)d : null)
            .Where(d => d != null).Select(d => d!.Value).ToHashSet(),
        From = form["schedule_from"].ToString().Trim(),
        To = form["schedule_to"].ToString().Trim(),
        Hide = form["schedule_hide"].ToString() == "true"
    };

    /// <summary>Validates the form and writes it onto the category. Returns the problems (empty when saved).</summary>
    public static List<string> Apply(Form f, Category c)
    {
        var errors = new List<string>();
        c.HideWhenUnavailable = f.Hide;
        if (!f.Enabled)
        {
            c.AvailableDays = AvailableDays.None;
            c.AvailableFrom = c.AvailableTo = null;
            return errors;
        }

        if (f.Days.Count == 0) errors.Add("Tick at least one day it is served.");

        TimeOnly? from = null, to = null;
        var hasFrom = f.From.Length > 0;
        var hasTo = f.To.Length > 0;
        if (hasFrom != hasTo)
        {
            errors.Add("Enter both a start and an end time, or leave both empty for the whole day.");
        }
        else if (hasFrom)
        {
            var formats = new[] { "HH:mm", "H:mm", "HH:mm:ss" };
            if (!TimeOnly.TryParseExact(f.From, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
                || !TimeOnly.TryParseExact(f.To, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
            {
                errors.Add("Use times like 08:00.");
            }
            else if (a == b)
            {
                errors.Add("The start and end time are the same. Leave both empty for the whole day.");
            }
            else
            {
                (from, to) = (a, b);
            }
        }

        var days = f.Days.Count == 7 ? AvailableDays.None
            : DayFlags.Where(d => f.Days.Contains(d.Day)).Aggregate(AvailableDays.None, (m, d) => m | d.Flag);
        if (errors.Count == 0 && days == AvailableDays.None && from == null)
        {
            errors.Add("Every day, all day is the same as no schedule. Pick days or times, or switch this off.");
        }

        if (errors.Count == 0)
        {
            c.AvailableDays = days;
            c.AvailableFrom = from;
            c.AvailableTo = to;
        }
        return errors;
    }
}
