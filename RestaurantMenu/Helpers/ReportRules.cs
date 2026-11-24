using System.Globalization;
using System.Text;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Reports (docs/PLAN-backoffice.md, phase 7): the days a report covers, the period it compares with,
/// the change between them, and CSV that is safe to open in a spreadsheet. Tested in ReportRulesTests.
/// </summary>
public static class ReportRules
{
    public const int DefaultDays = 7;
    public const int MaxDays = 366;

    /// <summary>The report's days (inclusive): the chosen ones in order, never after today, at most a year.</summary>
    public static (DateOnly From, DateOnly To) Period(string? from, string? to, DateOnly today)
    {
        static DateOnly? Read(string? s) =>
            DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        var f = Read(from);
        var t = Read(to);
        if (f == null || t == null) return (today.AddDays(1 - DefaultDays), today);

        var (a, b) = f <= t ? (f.Value, t.Value) : (t.Value, f.Value);
        if (b > today) b = today;
        if (a > b) a = b;
        if (b.DayNumber - a.DayNumber + 1 > MaxDays) a = b.AddDays(1 - MaxDays);
        return (a, b);
    }

    /// <summary>The same number of days just before the report.</summary>
    public static (DateOnly From, DateOnly To) Previous(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        var end = from.AddDays(-1);
        return (end.AddDays(1 - days), end);
    }

    /// <summary>The change in percent (one decimal); null when the previous period had nothing.</summary>
    public static decimal? Change(decimal current, decimal previous) =>
        previous == 0 ? null : Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// CSV for spreadsheets: cells with commas, quotes or line breaks are quoted, and a cell that starts like
    /// a formula (= + - @) gets a leading apostrophe so it is shown, never run.
    /// </summary>
    public static string Csv(IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append(string.Join(",", row.Select(Cell)));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Cell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@') v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
