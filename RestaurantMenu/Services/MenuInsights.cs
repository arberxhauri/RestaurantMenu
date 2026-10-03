using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Reads MenuEvents back as owner-facing numbers. Days are counted in the restaurant's
/// time zone (Analytics:TimeZone, default Europe/Tirane), so "today" means the
/// restaurant's today, not UTC's.
/// </summary>
public class MenuInsights
{
    public static readonly int[] Ranges = { 7, 30, 90 };

    private readonly ApplicationDbContext _db;
    private readonly TimeZoneInfo _zone;
    private readonly string _zoneId;

    public MenuInsights(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _zoneId = config["Analytics:TimeZone"] ?? "Europe/Tirane";
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(_zoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _zone = TimeZoneInfo.Utc;
            _zoneId = "UTC";
        }
    }

    public DateOnly Today => TodayIn(_zone);

    private static DateOnly TodayIn(TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));

    private DateTime StartUtc(DateOnly day) => StartUtc(day, _zone);

    private static DateTime StartUtc(DateOnly day, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone);

    public record Totals(int Views, int DishOpens, int Adds);
    public record Day(DateOnly Date, int Views, int DishOpens, int Adds);
    public record Dish(int ProductId, string Name, bool Removed, int Opens, int Adds);
    public record Language(string Code, int Views);

    public class BranchReport
    {
        public int Days { get; init; }
        public DateOnly From { get; init; }
        public DateOnly To { get; init; }
        public required IReadOnlyList<Day> Daily { get; init; }
        public required Totals Current { get; init; }
        public required Totals Previous { get; init; }
        public required IReadOnlyList<Dish> TopDishes { get; init; }
        public required IReadOnlyList<Language> Languages { get; init; }
        public int LanguageSwitches { get; init; }
    }

    // Shape of the grouped SQL below.
    private sealed class DayTypeCount
    {
        public DateOnly Day { get; set; }
        public int Type { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// The last <paramref name="days"/> days including today, and the same span before it for
    /// comparison. Days follow <paramref name="timeZone"/> (the branch's own zone) when it is
    /// a known zone, otherwise the configured default.
    /// </summary>
    public async Task<BranchReport> ForBranchAsync(int branchId, int days, string? timeZone = null)
    {
        var (zone, zoneId) = (_zone, _zoneId);
        if (Helpers.OpeningHours.IsKnownTimeZone(timeZone))
        {
            try { (zone, zoneId) = (TimeZoneInfo.FindSystemTimeZoneById(timeZone!), timeZone!); }
            catch (TimeZoneNotFoundException) { }
        }

        var to = TodayIn(zone);
        var from = to.AddDays(-(days - 1));
        var previousFrom = from.AddDays(-days);
        var fromUtc = StartUtc(from, zone);
        var toUtc = StartUtc(to.AddDays(1), zone);
        var previousFromUtc = StartUtc(previousFrom, zone);

        // One grouped query for both periods. The day is computed in Postgres in the
        // restaurant's zone; every value is a parameter.
        var counts = await _db.Database.SqlQuery<DayTypeCount>($"""
            SELECT ("CreatedUtc" AT TIME ZONE {zoneId})::date AS "Day", "Type"::int AS "Type", count(*)::int AS "Count"
            FROM "MenuEvents"
            WHERE "BranchId" = {branchId} AND "CreatedUtc" >= {previousFromUtc} AND "CreatedUtc" < {toUtc}
            GROUP BY 1, 2
            """).ToListAsync();

        int Sum(DateOnly start, DateOnly end, MenuEventType type) =>
            counts.Where(c => c.Day >= start && c.Day <= end && c.Type == (int)type).Sum(c => c.Count);
        Totals TotalsFor(DateOnly start, DateOnly end) => new(
            Sum(start, end, MenuEventType.View), Sum(start, end, MenuEventType.DishOpen), Sum(start, end, MenuEventType.AddToList));

        // Every day present, including empty ones, so the chart's x-axis is continuous.
        var daily = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new Day(d, Sum(d, d, MenuEventType.View), Sum(d, d, MenuEventType.DishOpen), Sum(d, d, MenuEventType.AddToList)))
            .ToList();

        var inRange = _db.MenuEvents.Where(e => e.BranchId == branchId && e.CreatedUtc >= fromUtc && e.CreatedUtc < toUtc);

        var dishCounts = await inRange
            .Where(e => e.ProductId != null && (e.Type == MenuEventType.DishOpen || e.Type == MenuEventType.AddToList))
            .GroupBy(e => new { e.ProductId, e.Type })
            .Select(g => new { g.Key.ProductId, g.Key.Type, Count = g.Count() })
            .ToListAsync();

        var productIds = dishCounts.Select(d => d.ProductId!.Value).Distinct().ToList();
        // Include deleted dishes: their past popularity is still real.
        var names = await _db.Products.IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id) && p.BranchId == branchId)
            .Select(p => new { p.Id, p.Name, p.IsDeleted })
            .ToDictionaryAsync(p => p.Id);

        var topDishes = productIds
            .Where(names.ContainsKey)
            .Select(id => new Dish(id, names[id].Name, names[id].IsDeleted,
                dishCounts.Where(d => d.ProductId == id && d.Type == MenuEventType.DishOpen).Sum(d => d.Count),
                dishCounts.Where(d => d.ProductId == id && d.Type == MenuEventType.AddToList).Sum(d => d.Count)))
            .OrderByDescending(d => d.Opens).ThenByDescending(d => d.Adds).ThenBy(d => d.Name)
            .Take(10)
            .ToList();

        var languages = await inRange
            .Where(e => e.Type == MenuEventType.View && e.Lang != null)
            .GroupBy(e => e.Lang!)
            .Select(g => new Language(g.Key, g.Count()))
            .ToListAsync();

        var switches = await inRange.CountAsync(e => e.Type == MenuEventType.LanguageSwitch);

        return new BranchReport
        {
            Days = days,
            From = from,
            To = to,
            Daily = daily,
            Current = TotalsFor(from, to),
            Previous = TotalsFor(previousFrom, from.AddDays(-1)),
            TopDishes = topDishes,
            Languages = languages.OrderByDescending(l => l.Views).ToList(),
            LanguageSwitches = switches
        };
    }

    /// <summary>Last 7 days vs the 7 before, per branch, for the dashboard.</summary>
    public async Task<Dictionary<int, (Totals Current, Totals Previous)>> WeekByBranchAsync(IReadOnlyCollection<int> branchIds)
    {
        var to = Today;
        var from = to.AddDays(-6);
        var fromUtc = StartUtc(from);
        var previousFromUtc = StartUtc(from.AddDays(-7));
        var toUtc = StartUtc(to.AddDays(1));

        var rows = await _db.MenuEvents
            .Where(e => branchIds.Contains(e.BranchId) && e.CreatedUtc >= previousFromUtc && e.CreatedUtc < toUtc
                        && e.Type != MenuEventType.LanguageSwitch)
            .GroupBy(e => new { e.BranchId, e.Type, Current = e.CreatedUtc >= fromUtc })
            .Select(g => new { g.Key.BranchId, g.Key.Type, g.Key.Current, Count = g.Count() })
            .ToListAsync();

        Totals For(int branchId, bool current)
        {
            int Count(MenuEventType t) => rows.Where(r => r.BranchId == branchId && r.Current == current && r.Type == t).Sum(r => r.Count);
            return new Totals(Count(MenuEventType.View), Count(MenuEventType.DishOpen), Count(MenuEventType.AddToList));
        }

        return branchIds.ToDictionary(id => id, id => (For(id, true), For(id, false)));
    }

    /// <summary>"+12%", "−5%", "New" or null when there's nothing to compare.</summary>
    public static (string Text, int Direction)? Change(int current, int previous)
    {
        if (previous == 0) return current == 0 ? null : ("New", 1);
        var pct = (int)Math.Round((current - previous) * 100.0 / previous);
        return pct == 0 ? ("No change", 0) : (pct > 0 ? $"+{pct}%" : $"−{-pct}%", Math.Sign(pct));
    }
}
