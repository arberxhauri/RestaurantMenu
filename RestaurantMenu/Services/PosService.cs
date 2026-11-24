using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

public sealed record PosBillLine(int? ProductId, string Name, string? Options, int Qty, decimal UnitPrice, decimal Total);

/// <summary>
/// An open bill: a table's open orders together (guest QR orders and POS orders), or one takeaway
/// order. <see cref="Key"/> addresses it (PosRules.BillKey).
/// </summary>
public sealed record PosBill(string Key, string Label, int? TableId, int Guests, bool BillRequested, DateTime OpenedUtc,
    IReadOnlyList<int> OrderIds, IReadOnlyList<int> OrderNumbers, IReadOnlyList<PosBillLine> Lines,
    string? DiscountName, decimal Subtotal, decimal Discount, decimal Total, bool AllServed);

public sealed record PosReceipt(PosBill Bill, decimal Change);

public sealed record CashLine(DateTime Utc, PaymentMethod Method, decimal Amount, decimal Tip, string? By, int OrderNumber);

public sealed record CashReport(DateOnly Day, IReadOnlyDictionary<PaymentMethod, decimal> ByMethod, decimal Tips,
    decimal Discounts, int Bills, IReadOnlyList<CashLine> Payments)
{
    public decimal Total => ByMethod.Values.Sum();
}

/// <summary>
/// The POS's bills (docs/PLAN-backoffice.md, phase 6): open bills, a discount per bill, asking for the
/// bill, taking payment (which closes the bill's orders) and the day's cash report. Payments are the
/// restaurant's own record, not fiscal receipts (plan §8). Every method checks the branch itself.
/// </summary>
public sealed class PosService
{
    /// <summary>Orders older than this never show as open bills (orders from before the POS were never closed).</summary>
    public static readonly TimeSpan OpenWindow = TimeSpan.FromHours(18);
    public const decimal MaxTip = 1000m;

    private readonly ApplicationDbContext _db;

    public PosService(ApplicationDbContext db) => _db = db;

    /// <summary>Every open bill of the branch, oldest first.</summary>
    public async Task<List<PosBill>> OpenBillsAsync(int branchId, DateTime utcNow)
    {
        var orders = await OpenOrders(branchId, utcNow).AsNoTracking().ToListAsync();
        return orders.GroupBy(o => o.TableId is { } t ? PosRules.BillKey(t, null) : PosRules.BillKey(null, o.Id))
            .Select(g => ToBill(g.Key, g.ToList()))
            .OrderBy(b => b.OpenedUtc)
            .ToList();
    }

    public async Task<PosBill?> BillAsync(int branchId, string key, DateTime utcNow)
    {
        var orders = await BillOrders(branchId, key, utcNow).AsNoTracking().ToListAsync();
        return orders.Count == 0 ? null : ToBill(key, orders);
    }

    /// <summary>Puts one discount on the bill (replacing any other), or takes it off with null.</summary>
    public async Task<(PosBill? Bill, string? Problem)> SetDiscountAsync(int branchId, string key, int? discountId,
        string? userId, TimeZoneInfo zone, DateTime utcNow)
    {
        var orders = await BillOrders(branchId, key, utcNow).ToListAsync();
        if (orders.Count == 0) return (null, null);

        _db.OrderDiscounts.RemoveRange(orders.SelectMany(o => o.Discounts));
        if (discountId != null)
        {
            var discount = await _db.Discounts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == discountId && d.BranchId == branchId);
            if (discount == null) return (null, null);
            if (!PosRules.DiscountActive(discount, TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone)))
                return (ToBill(key, orders), $"{discount.Name} doesn't apply right now.");
            var amount = PosRules.DiscountAmount(discount, Lines(orders));
            if (amount <= 0) return (ToBill(key, orders), $"{discount.Name} doesn't apply to anything on this bill.");
            orders[0].Discounts.Add(new OrderDiscount
            {
                DiscountId = discount.Id, Name = discount.Name, Amount = amount, AppliedById = userId, CreatedUtc = utcNow
            });
        }
        await _db.SaveChangesAsync();
        return (ToBill(key, orders), null);
    }

    /// <summary>Marks the bill as asked for (the floor plan shows it), or clears that.</summary>
    public async Task<PosBill?> RequestBillAsync(int branchId, string key, bool requested, DateTime utcNow)
    {
        var orders = await BillOrders(branchId, key, utcNow).ToListAsync();
        if (orders.Count == 0) return null;
        foreach (var o in orders) o.BillRequestedUtc = requested ? o.BillRequestedUtc ?? utcNow : null;
        await _db.SaveChangesAsync();
        return ToBill(key, orders);
    }

    /// <summary>
    /// Takes payment for the whole bill and closes its orders. Two screens paying the same bill at once
    /// can't both succeed: the orders are closed in one update, and payments are saved only if all were
    /// still open.
    /// </summary>
    public async Task<(PosReceipt? Receipt, string? Problem)> PayAsync(int branchId, string key, IReadOnlyList<Tender> tenders,
        decimal tip, string? userId, string? userName, DateTime utcNow)
    {
        if (tip < 0 || tip > MaxTip) return (null, "Enter a tip between 0 and 1000.");
        var orders = await BillOrders(branchId, key, utcNow).AsNoTracking().ToListAsync();
        if (orders.Count == 0) return (null, null);

        var bill = ToBill(key, orders);
        var check = PosRules.CheckPayment(bill.Total, tenders);
        if (!check.Ok) return (null, check.Problem);

        var ids = orders.Select(o => o.Id).ToList();
        var payments = Applied(tenders, check.Change)
            .Select((t, i) => new Payment
            {
                OrderId = ids[0], Method = t.Method, Amount = t.Amount, Tip = i == 0 ? tip : 0m,
                UserId = userId, UserName = userName, CreatedUtc = utcNow
            }).ToList();

        var strategy = _db.Database.CreateExecutionStrategy();
        var paid = await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var closed = await _db.Orders.Where(o => ids.Contains(o.Id) && o.ClosedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ClosedUtc, utcNow).SetProperty(o => o.UpdatedUtc, utcNow));
            if (closed != ids.Count)
            {
                await tx.RollbackAsync();
                return false;
            }
            _db.Payments.AddRange(payments);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        });
        return paid ? (new PosReceipt(bill, check.Change), null) : (null, "This bill was just paid on another screen.");
    }

    /// <summary>The day's payments by method, tips, discounts and bills (the branch's local day).</summary>
    public async Task<CashReport> CashReportAsync(int branchId, DateOnly day, TimeZoneInfo zone)
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone);
        var to = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Order!.BranchId == branchId && p.CreatedUtc >= from && p.CreatedUtc < to)
            .OrderBy(p => p.CreatedUtc)
            .Select(p => new { p.OrderId, p.Method, p.Amount, p.Tip, p.UserName, p.CreatedUtc, p.Order!.Number })
            .ToListAsync();
        var orderIds = payments.Select(p => p.OrderId).Distinct().ToList();
        var discounts = await _db.OrderDiscounts.AsNoTracking()
            .Where(d => d.Order!.BranchId == branchId && d.Order.ClosedUtc >= from && d.Order.ClosedUtc < to)
            .SumAsync(d => (decimal?)d.Amount) ?? 0m;

        var byMethod = Enum.GetValues<PaymentMethod>().ToDictionary(m => m, m => payments.Where(p => p.Method == m).Sum(p => p.Amount));
        return new CashReport(day, byMethod, payments.Sum(p => p.Tip), discounts, orderIds.Count,
            payments.Select(p => new CashLine(DateTime.SpecifyKind(p.CreatedUtc, DateTimeKind.Utc), p.Method, p.Amount, p.Tip, p.UserName, p.Number)).ToList());
    }

    // ---------- helpers ----------

    private IQueryable<Order> OpenOrders(int branchId, DateTime utcNow)
    {
        var since = utcNow - OpenWindow;
        return _db.Orders.Include(o => o.Items).Include(o => o.Discounts).AsSplitQuery()
            .Where(o => o.BranchId == branchId && o.ClosedUtc == null && o.Status != OrderStatus.Cancelled && o.CreatedUtc > since)
            .OrderBy(o => o.CreatedUtc);
    }

    private IQueryable<Order> BillOrders(int branchId, string key, DateTime utcNow)
    {
        var (tableId, orderId) = PosRules.ParseBillKey(key);
        var open = OpenOrders(branchId, utcNow);
        if (tableId is { } t) return open.Where(o => o.TableId == t);
        if (orderId is { } id) return open.Where(o => o.Id == id && o.TableId == null);
        return open.Where(_ => false);
    }

    private static List<BillLine> Lines(IEnumerable<Order> orders) =>
        orders.SelectMany(o => o.Items).Select(i => new BillLine(i.ProductId, i.UnitPrice, i.Quantity)).ToList();

    private static PosBill ToBill(string key, IReadOnlyList<Order> orders)
    {
        var first = orders[0];
        var discount = orders.SelectMany(o => o.Discounts).ToList();
        var summary = PosRules.Summarize(Lines(orders), discount.Sum(d => d.Amount));
        var label = first.TableId != null
            ? $"Table {first.TableNumber}{(string.IsNullOrWhiteSpace(first.TableName) ? "" : " · " + first.TableName)}"
            : $"Takeaway #{first.Number}";
        var lines = orders.SelectMany(o => o.Items)
            .GroupBy(i => (i.ProductId, i.Name, i.Options, i.UnitPrice))
            .Select(g => new PosBillLine(g.Key.ProductId, g.Key.Name, g.Key.Options, g.Sum(i => i.Quantity), g.Key.UnitPrice,
                g.Key.UnitPrice * g.Sum(i => i.Quantity)))
            .ToList();
        return new PosBill(key, label, first.TableId, orders.Max(o => o.Guests), orders.Any(o => o.BillRequestedUtc != null),
            DateTime.SpecifyKind(first.CreatedUtc, DateTimeKind.Utc), orders.Select(o => o.Id).ToList(), orders.Select(o => o.Number).ToList(),
            lines, discount.FirstOrDefault()?.Name, summary.Subtotal, summary.Discount, summary.Total,
            orders.All(o => o.Status == OrderStatus.Served));
    }

    /// <summary>What each tender actually pays: the change comes off the cash.</summary>
    private static IEnumerable<Tender> Applied(IReadOnlyList<Tender> tenders, decimal change)
    {
        var left = change;
        foreach (var t in tenders.Reverse())
        {
            if (t.Method == PaymentMethod.Cash && left > 0)
            {
                var back = Math.Min(left, t.Amount);
                left -= back;
                if (t.Amount - back > 0) yield return t with { Amount = t.Amount - back };
            }
            else
            {
                yield return t;
            }
        }
    }
}
