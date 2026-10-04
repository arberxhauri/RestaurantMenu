using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

public record TopDish(string Name, int Qty, decimal Revenue);

/// <summary>One business day of a branch, stored (rolled up) or computed live (today).</summary>
public record DaySales(DateOnly Date, int Orders, int Items, decimal Revenue, int Cancelled, IReadOnlyList<TopDish> Top,
    decimal? Cash, decimal? Card, string? CloseNote, string? ClosedBy, DateTime? ClosedUtc, bool Live)
{
    public decimal AverageOrder => Orders == 0 ? 0 : Math.Round(Revenue / Orders, 2);
    public decimal? Counted => Cash == null && Card == null ? null : (Cash ?? 0) + (Card ?? 0);
}

/// <summary>
/// End-of-day sales: table orders rolled up per business day (the order's day in the
/// branch's time zone), stored as DailySales by the nightly roll-up and on every close,
/// plus the manager's count of cash and card at closing.
/// </summary>
public class SalesService
{
    private readonly ApplicationDbContext _db;
    public SalesService(ApplicationDbContext db) => _db = db;

    public async Task<DaySales> ComputeAsync(int branchId, DateOnly date)
    {
        var orders = await _db.Orders.AsNoTracking().Where(o => o.BranchId == branchId && o.OrderDay == date)
            .Select(o => new { o.Status, o.Total, Items = o.Items.Sum(i => i.Quantity) }).ToListAsync();
        var live = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        var top = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order!.BranchId == branchId && i.Order.OrderDay == date && i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.Name)
            .Select(g => new { Name = g.Key, Qty = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.UnitPrice * i.Quantity) })
            .OrderByDescending(x => x.Qty).ThenByDescending(x => x.Revenue).Take(5).ToListAsync();
        return new DaySales(date, live.Count, live.Sum(o => o.Items), live.Sum(o => o.Total), orders.Count - live.Count,
            top.Select(t => new TopDish(t.Name, t.Qty, t.Revenue)).ToList(), null, null, null, null, null, true);
    }

    /// <summary>Stores (or refreshes) a day's figures, keeping what the manager entered at closing.</summary>
    public async Task<DailySales> RollUpAsync(int branchId, DateOnly date, DateTime utcNow)
    {
        var s = await ComputeAsync(branchId, date);
        for (var attempt = 0; ; attempt++)
        {
            var row = await _db.DailySales.FirstOrDefaultAsync(d => d.BranchId == branchId && d.Date == date);
            if (row == null)
            {
                row = new DailySales { BranchId = branchId, Date = date };
                _db.DailySales.Add(row);
            }
            row.Orders = s.Orders;
            row.Items = s.Items;
            row.Revenue = s.Revenue;
            row.CancelledOrders = s.Cancelled;
            row.TopDishes = JsonSerializer.Serialize(s.Top);
            row.ComputedUtc = utcNow;
            try
            {
                await _db.SaveChangesAsync();
                return row;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && attempt < 2)
            {
                _db.ChangeTracker.Clear(); // another roll-up stored it at the same moment: update that one
            }
        }
    }

    public async Task<DailySales> CloseDayAsync(int branchId, DateOnly date, decimal? cash, decimal? card, string? note, string? closedBy, DateTime utcNow)
    {
        var row = await RollUpAsync(branchId, date, utcNow);
        row.CashTotal = cash;
        row.CardTotal = card;
        row.CloseNote = note;
        row.ClosedByName = closedBy;
        row.ClosedUtc = utcNow;
        await _db.SaveChangesAsync();
        return row;
    }

    /// <summary>Every day from <paramref name="from"/> to <paramref name="to"/>: stored figures, live ones for today and days not rolled up yet.</summary>
    public async Task<List<DaySales>> DaysAsync(int branchId, DateOnly from, DateOnly to, DateOnly today)
    {
        var stored = await _db.DailySales.AsNoTracking().Where(d => d.BranchId == branchId && d.Date >= from && d.Date <= to).ToDictionaryAsync(d => d.Date);
        var orderDays = await _db.Orders.AsNoTracking().Where(o => o.BranchId == branchId && o.OrderDay >= from && o.OrderDay <= to)
            .Select(o => o.OrderDay).Distinct().ToListAsync();
        var list = new List<DaySales>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d != today && stored.TryGetValue(d, out var row))
            {
                list.Add(FromRow(row));
            }
            else if (d == today || orderDays.Contains(d))
            {
                var live = await ComputeAsync(branchId, d);
                list.Add(stored.TryGetValue(d, out var r2)
                    ? live with { Cash = r2.CashTotal, Card = r2.CardTotal, CloseNote = r2.CloseNote, ClosedBy = r2.ClosedByName, ClosedUtc = r2.ClosedUtc }
                    : live);
            }
            else
            {
                list.Add(new DaySales(d, 0, 0, 0, 0, Array.Empty<TopDish>(), null, null, null, null, null, false));
            }
        }
        return list;
    }

    public static DaySales FromRow(DailySales r) => new(r.Date, r.Orders, r.Items, r.Revenue, r.CancelledOrders,
        string.IsNullOrEmpty(r.TopDishes) ? Array.Empty<TopDish>() : JsonSerializer.Deserialize<List<TopDish>>(r.TopDishes) ?? new List<TopDish>(),
        r.CashTotal, r.CardTotal, r.CloseNote, r.ClosedByName, r.ClosedUtc, false);
}

/// <summary>
/// The nightly roll-up: every 30 minutes, for each branch, stores each finished day that has
/// orders and isn't stored yet (35 days back), and refreshes days for 6 hours after they end
/// (a late cancellation still counts). Idempotent, so running on several instances is harmless.
/// </summary>
public class SalesRollupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SalesRollupService> _logger;
    private readonly TimeSpan _interval;

    public SalesRollupService(IServiceScopeFactory scopes, ILogger<SalesRollupService> logger, IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Sales:RollupIntervalSeconds", 1800), 10, 86400));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var n = await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                    if (n > 0) _logger.LogInformation("Sales roll-up: {Count} day(s) stored", n);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Sales roll-up failed; retrying later");
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public async Task<int> RunOnceAsync(DateTime utcNow, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var count = 0;
        foreach (var b in await db.Branches.AsNoTracking().Select(b => new { b.Id, b.TimeZone }).ToListAsync(ct))
        {
            var zone = OpeningHours.Zone(b.TimeZone);
            var today = BookingRules.Today(zone, utcNow);
            var from = today.AddDays(-35);
            var stored = await db.DailySales.AsNoTracking().Where(d => d.BranchId == b.Id && d.Date >= from)
                .ToDictionaryAsync(d => d.Date, d => d.ComputedUtc, ct);
            var withOrders = await db.Orders.AsNoTracking().Where(o => o.BranchId == b.Id && o.OrderDay >= from && o.OrderDay < today)
                .Select(o => o.OrderDay).Distinct().ToListAsync(ct);
            foreach (var day in withOrders.Concat(stored.Keys.Where(k => k < today)).Distinct().OrderBy(d => d))
            {
                var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
                var fresh = stored.TryGetValue(day, out var computed) && computed >= dayEndUtc.AddHours(6);
                if (fresh) continue;
                await sales.RollUpAsync(b.Id, day, utcNow);
                count++;
            }
        }
        return count;
    }
}
