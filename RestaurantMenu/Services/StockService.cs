using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Stock levels change only here, always with a StockMovement saying why, by whom and the
/// level after. Each change locks the ingredient rows for its transaction, so two people
/// (or an order and a delivery) can't overwrite each other.
/// Table orders use stock through recipes: <see cref="SyncOrderAsync"/> makes an order's
/// movements add up to its recipes while it stands, and to nothing once cancelled, however
/// often it goes back and forth.
/// </summary>
public class StockService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<StockService> _logger;

    public StockService(ApplicationDbContext db, ILogger<StockService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Records a delivery/usage/waste (<paramref name="amount"/> is how much, positive) or a
    /// count (<paramref name="amount"/> is the counted level). Returns the movement, or null
    /// when the ingredient isn't this branch's.
    /// </summary>
    public async Task<StockMovement?> RecordAsync(int branchId, int ingredientId, StockMovementKind kind, decimal amount, string? note,
        string? userId, string? userName, DateTime utcNow)
    {
        if (kind is StockMovementKind.Sale or StockMovementKind.SaleReturn) throw new ArgumentException("Orders move stock themselves.");
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var current = await LockAsync(branchId, new[] { ingredientId });
            if (!current.TryGetValue(ingredientId, out var level)) return null;

            var change = kind switch
            {
                StockMovementKind.Delivery => amount,
                StockMovementKind.Count => amount - level,
                _ => -amount
            };
            var m = await ApplyAsync(branchId, ingredientId, level, change, kind, note, null, userId, userName, utcNow);
            await tx.CommitAsync();
            return m;
        });
    }

    /// <summary>Locks the ingredients (in id order, so concurrent calls never deadlock) and returns their levels.</summary>
    private async Task<Dictionary<int, decimal>> LockAsync(int branchId, IEnumerable<int> ids)
    {
        var list = ids.Distinct().OrderBy(i => i).ToArray();
        var rows = await _db.Database.SqlQuery<LevelRow>($"""
            SELECT "Id", "Quantity" FROM "Ingredients"
            WHERE "BranchId" = {branchId} AND "Id" = ANY({list})
            ORDER BY "Id" FOR UPDATE
            """).ToListAsync();
        return rows.ToDictionary(r => r.Id, r => r.Quantity);
    }

    private class LevelRow { public int Id { get; set; } public decimal Quantity { get; set; } }

    private async Task<StockMovement> ApplyAsync(int branchId, int ingredientId, decimal level, decimal change, StockMovementKind kind,
        string? note, int? orderId, string? userId, string? userName, DateTime utcNow)
    {
        var after = level + change;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Ingredients" SET "Quantity" = {after} WHERE "Id" = {ingredientId}""");
        var m = new StockMovement
        {
            BranchId = branchId, IngredientId = ingredientId, Kind = kind, Change = change, QuantityAfter = after,
            Note = note, OrderId = orderId, UserId = userId, UserName = userName, CreatedUtc = utcNow
        };
        _db.StockMovements.Add(m);
        await _db.SaveChangesAsync();
        return m;
    }

    /// <summary>
    /// Brings an order's stock movements in line with its state: standing orders use their
    /// dishes' recipes, cancelled ones use nothing. Safe to call any number of times.
    /// Never throws: stock must not stop an order.
    /// </summary>
    public async Task SyncOrderAsync(int orderId, DateTime utcNow)
    {
        try
        {
            var order = await _db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return;

            // What the order should have used.
            var want = new Dictionary<int, decimal>();
            if (order.Status != OrderStatus.Cancelled)
            {
                var qty = order.Items.Where(i => i.ProductId != null).GroupBy(i => i.ProductId!.Value).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
                var productIds = qty.Keys.ToList();
                var lines = await _db.DishIngredients.AsNoTracking()
                    .Where(d => productIds.Contains(d.ProductId) && d.Ingredient!.BranchId == order.BranchId)
                    .Select(d => new { d.ProductId, d.IngredientId, d.Quantity }).ToListAsync();
                foreach (var l in lines)
                    want[l.IngredientId] = (want.TryGetValue(l.IngredientId, out var w) ? w : 0) - l.Quantity * qty[l.ProductId];
            }

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                // What it has used so far (read after taking the locks, so concurrent syncs see each other).
                var touched = await _db.StockMovements.AsNoTracking().Where(m => m.OrderId == orderId).Select(m => m.IngredientId).Distinct().ToListAsync();
                var levels = await LockAsync(order.BranchId, want.Keys.Concat(touched));
                var have = await _db.StockMovements.AsNoTracking().Where(m => m.OrderId == orderId)
                    .GroupBy(m => m.IngredientId).Select(g => new { g.Key, Sum = g.Sum(m => m.Change) }).ToDictionaryAsync(x => x.Key, x => x.Sum);

                foreach (var id in levels.Keys)
                {
                    var delta = (want.TryGetValue(id, out var w) ? w : 0) - (have.TryGetValue(id, out var h) ? h : 0);
                    if (delta == 0) continue;
                    await ApplyAsync(order.BranchId, id, levels[id], delta, delta < 0 ? StockMovementKind.Sale : StockMovementKind.SaleReturn,
                        $"Order #{order.Number}, table {order.TableNumber}", orderId, null, null, utcNow);
                }
                await tx.CommitAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stock sync failed for order {OrderId}", orderId);
            _db.ChangeTracker.Clear();
        }
    }
}
