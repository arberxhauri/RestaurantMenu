using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>A period's headline numbers.</summary>
public sealed record ReportTotals(decimal Revenue, int Orders, decimal AverageBill, decimal Discounts, decimal Waste);

/// <summary>A dish or category: how many were sold and what they brought in.</summary>
public sealed record ReportRow(string Name, int Quantity, decimal Revenue);

public sealed record ReportPage(DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo,
    ReportTotals Now, ReportTotals Before, IReadOnlyDictionary<PaymentMethod, decimal> Payments,
    IReadOnlyList<ReportRow> Dishes, IReadOnlyList<ReportRow> Categories, IReadOnlyList<decimal> ByHour,
    IReadOnlyList<decimal> ByWeekday);

/// <summary>
/// The branch's reports (docs/PLAN-backoffice.md, phase 7): sales, orders, discounts and waste against the
/// previous period, payments by method, best-selling dishes and categories, and sales by hour and weekday.
/// Cancelled orders never count. Days are the branch's local days (Order.OrderDay).
/// </summary>
public sealed class ReportService
{
    public const int TopDishes = 15;

    private readonly ApplicationDbContext _db;

    public ReportService(ApplicationDbContext db) => _db = db;

    public async Task<ReportPage> BuildAsync(int branchId, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var (prevFrom, prevTo) = Helpers.ReportRules.Previous(from, to);
        var orders = await Orders(branchId, from, to).Include(o => o.Items).AsSplitQuery().AsNoTracking().ToListAsync();

        var productIds = orders.SelectMany(o => o.Items).Where(i => i.ProductId != null).Select(i => i.ProductId!.Value).Distinct().ToList();
        // Deleted dishes still belong to their category in old orders.
        var categoryOf = await _db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BranchId == branchId && productIds.Contains(p.Id))
            .Select(p => new { p.Id, Category = p.Category != null ? p.Category.Name : null })
            .ToDictionaryAsync(p => p.Id, p => p.Category ?? "Other");

        var items = orders.SelectMany(o => o.Items).ToList();
        var dishes = items.GroupBy(i => i.Name)
            .Select(g => new ReportRow(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)))
            .OrderByDescending(r => r.Revenue).Take(TopDishes).ToList();
        var categories = items.GroupBy(i => i.ProductId is { } id && categoryOf.TryGetValue(id, out var c) ? c : "Other")
            .Select(g => new ReportRow(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)))
            .OrderByDescending(r => r.Revenue).ToList();

        var byHour = new decimal[24];
        var byWeekday = new decimal[7]; // Monday first
        foreach (var o in orders)
        {
            byHour[TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(o.CreatedUtc, DateTimeKind.Utc), zone).Hour] += o.Total;
            byWeekday[((int)o.OrderDay.DayOfWeek + 6) % 7] += o.Total;
        }

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Order!.BranchId == branchId && p.Order.OrderDay >= from && p.Order.OrderDay <= to)
            .GroupBy(p => p.Method).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum);

        return new ReportPage(from, to, prevFrom, prevTo,
            await TotalsAsync(branchId, from, to, zone), await TotalsAsync(branchId, prevFrom, prevTo, zone),
            Enum.GetValues<PaymentMethod>().ToDictionary(m => m, m => payments.GetValueOrDefault(m)),
            dishes, categories, byHour, byWeekday);
    }

    /// <summary>Sales after discounts, order count, average bill, discounts and the cost of recorded waste.</summary>
    public async Task<ReportTotals> TotalsAsync(int branchId, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var orders = Orders(branchId, from, to);
        var gross = await orders.SumAsync(o => (decimal?)o.Total) ?? 0m;
        var count = await orders.CountAsync();
        var discounts = await _db.OrderDiscounts.AsNoTracking()
            .Where(d => d.Order!.BranchId == branchId && d.Order.Status != OrderStatus.Cancelled
                && d.Order.OrderDay >= from && d.Order.OrderDay <= to)
            .SumAsync(d => (decimal?)d.Amount) ?? 0m;

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var waste = await _db.StockMovements.AsNoTracking()
            .Where(m => m.BranchId == branchId && m.Kind == StockMovementKind.Waste && m.CreatedUtc >= startUtc && m.CreatedUtc < endUtc
                && m.Ingredient!.CostPerUnit != null)
            .SumAsync(m => (decimal?)(-m.Change * m.Ingredient!.CostPerUnit!.Value)) ?? 0m;

        var revenue = gross - discounts;
        return new ReportTotals(revenue, count, count == 0 ? 0m : Math.Round(revenue / count, 2), discounts, Math.Max(0m, waste));
    }

    private IQueryable<Order> Orders(int branchId, DateOnly from, DateOnly to) =>
        _db.Orders.Where(o => o.BranchId == branchId && o.Status != OrderStatus.Cancelled && o.OrderDay >= from && o.OrderDay <= to);
}
