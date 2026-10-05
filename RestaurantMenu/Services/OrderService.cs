using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Hubs;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>An order as the kitchen display shows it (JSON for kitchen.js and the hub).</summary>
public record KitchenOrder(int Id, int Number, int Table, string? TableName, string Status, DateTime Created, DateTime Updated,
    string? Note, decimal Total, IReadOnlyList<KitchenOrderItem> Items)
{
    public static KitchenOrder From(Order o) => new(o.Id, o.Number, o.TableNumber, o.TableName, StatusId(o.Status),
        DateTime.SpecifyKind(o.CreatedUtc, DateTimeKind.Utc), DateTime.SpecifyKind(o.UpdatedUtc, DateTimeKind.Utc), o.Note, o.Total,
        o.Items.OrderBy(i => i.Id).Select(i => new KitchenOrderItem(i.Quantity, i.Name, i.Options)).ToList());

    public static string StatusId(OrderStatus s) => s.ToString().ToLowerInvariant();
}

public record KitchenOrderItem(int Qty, string Name, string? Options);

/// <summary>What a guest's phone sees about its own order.</summary>
public record GuestOrder(Guid Id, int Number, int Table, string Status, string StatusText, decimal Total, int Count);

public record PlaceResult(Order? Order, OrderProblem? Problem, string? Message, IReadOnlyList<int> Unavailable);

/// <summary>
/// Table orders: placing them (every check a guest's phone could get wrong or fake),
/// moving them through the kitchen, and telling kitchen displays about each change.
/// </summary>
public class OrderService
{
    /// <summary>Orders a table may send in <see cref="BusyWindow"/>; past that, "wait a minute".</summary>
    public const int MaxOrdersPerTable = 8;
    public static readonly TimeSpan BusyWindow = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly IHubContext<KitchenHub> _hub;
    private readonly ILogger<OrderService> _logger;
    private readonly StockService _stock;
    private readonly IEntitlementService _entitlements;

    public OrderService(ApplicationDbContext db, IHubContext<KitchenHub> hub, ILogger<OrderService> logger, StockService stock, IEntitlementService entitlements)
    {
        _entitlements = entitlements;
        _db = db;
        _hub = hub;
        _logger = logger;
        _stock = stock;
    }

    private static PlaceResult Fail(OrderProblem p, string? lang, IReadOnlyList<int>? ids = null, string? message = null) =>
        new(null, p, message ?? OrderText.Problem(p, lang), ids ?? Array.Empty<int>());

    public async Task<PlaceResult> PlaceAsync(OrderRequest req, DateTime utcNow)
    {
        var lang = req.Lang;
        if (req.RequestId == Guid.Empty || SeoService.ValidTable(req.Table) == null || req.Items == null)
            return Fail(OrderProblem.Invalid, lang);

        var branch = await _db.Branches.AsNoTracking().Include(b => b.OpeningHours)
            .FirstOrDefaultAsync(b => b.Id == req.Branch);
        if (branch == null || !branch.OrderingEnabled || branch.OrdersPaused) return Fail(OrderProblem.Unavailable, lang);
        // The plan: without table ordering (or a paused branch, or a read-only account) nothing reaches the kitchen.
        if ((await _entitlements.ForBranchAsync(branch.Id))?.Has(BillingModule.Ordering) != true) return Fail(OrderProblem.Unavailable, lang);

        var languages = branch.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        lang = lang != null && languages.Contains(lang) ? lang : languages.FirstOrDefault() ?? "en";

        var table = await _db.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.BranchId == branch.Id && t.Number == req.Table);
        if (table == null || !OrderRules.CodeMatches(table.Code, req.Code)) return Fail(OrderProblem.Unavailable, lang);

        // The same attempt again (double tap, lost response): answer with what was saved.
        var existing = await LoadByRequestAsync(branch.Id, req.RequestId);
        if (existing != null) return new PlaceResult(existing, null, null, Array.Empty<int>());

        var zone = OpeningHours.Zone(branch.TimeZone);
        if (branch.HoursEnabled && !OpeningHours.GetStatus(branch.OpeningHours ?? new List<BranchHours>(), zone, utcNow).IsOpen)
            return Fail(OrderProblem.Closed, lang);

        var since = utcNow - BusyWindow;
        if (await _db.Orders.CountAsync(o => o.BranchId == branch.Id && o.TableId == table.Id && o.CreatedUtc > since) >= MaxOrdersPerTable)
            return Fail(OrderProblem.Busy, lang);

        var ids = req.Items.Select(i => i.Id).Distinct().Take(OrderRules.MaxLines + 1).ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.BranchId == branch.Id && ids.Contains(p.Id))
            .Include(p => p.Category)
            .Include(p => p.OptionGroups!).ThenInclude(g => g.Options)
            .AsSplitQuery()
            .ToDictionaryAsync(p => p.Id);

        var (lines, problem, unavailable) = OrderRules.Price(req.Items, products,
            p => p.Category == null || ServingTimes.Status(p.Category, zone, utcNow) is not { IsOpen: false });
        if (problem == OrderProblem.NotAvailableNow)
        {
            var names = unavailable.Select(id => TranslationHelper.GetTranslation(products[id].Name, products[id].NameTranslations, lang));
            return Fail(OrderProblem.NotAvailableNow, lang, unavailable, string.Format(OrderText.For(lang).NotNow, string.Join(", ", names)));
        }
        if (problem != null) return Fail(problem.Value, lang);

        var order = new Order
        {
            PublicId = Guid.NewGuid(),
            ClientRequestId = req.RequestId,
            BranchId = branch.Id,
            TableId = table.Id,
            TableNumber = table.Number,
            TableName = table.Name,
            OrderDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone)),
            Status = OrderStatus.New,
            Note = OrderRules.CleanNote(req.Note),
            Language = lang,
            Total = lines.Sum(l => l.UnitPrice * l.Quantity),
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow,
            Items = lines.Select(l => new OrderItem
            {
                ProductId = l.ProductId, Name = l.Name, Options = l.Options, OptionIds = l.OptionIds,
                UnitPrice = l.UnitPrice, Quantity = l.Quantity
            }).ToList()
        };
        _db.Orders.Add(order);

        order.Number = await NextNumberAsync(branch.Id, order.OrderDay);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: var name }
                                           && name?.Contains("ClientRequestId") == true)
        {
            // The same attempt arrived twice at once and the other copy was saved first.
            _db.ChangeTracker.Clear();
            var saved = await LoadByRequestAsync(branch.Id, req.RequestId);
            if (saved != null) return new PlaceResult(saved, null, null, Array.Empty<int>());
            throw;
        }

        await NotifyAsync(order);
        // Recipes use their ingredients (Stock); best effort, never stops the order.
        await _stock.SyncOrderAsync(order.Id, utcNow);
        return new PlaceResult(order, null, null, Array.Empty<int>());
    }

    /// <summary>
    /// The branch's next order number for <paramref name="day"/> (1 on a new day), from a
    /// single UPDATE: Postgres locks the branch row for the statement, so simultaneous
    /// orders queue for a number instead of colliding. A number whose order then fails to
    /// save is simply skipped.
    /// </summary>
    private async Task<int> NextNumberAsync(int branchId, DateOnly day)
    {
        var rows = await _db.Database.SqlQuery<int>($"""
            UPDATE "Branches"
            SET "OrderCounter" = CASE WHEN "OrderCounterDay" = {day} THEN "OrderCounter" + 1 ELSE 1 END,
                "OrderCounterDay" = {day}
            WHERE "Id" = {branchId}
            RETURNING "OrderCounter" AS "Value"
            """).ToListAsync();
        return rows.Count == 1 ? rows[0] : throw new InvalidOperationException($"Branch {branchId} not found for numbering.");
    }

    private Task<Order?> LoadByRequestAsync(int branchId, Guid requestId) =>
        _db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.BranchId == branchId && o.ClientRequestId == requestId);

    /// <summary>Statuses for a guest's own orders (they hold the ids), newest first.</summary>
    public async Task<List<GuestOrder>> GuestStatusAsync(IReadOnlyCollection<Guid> ids, string? lang)
    {
        if (ids.Count == 0) return new List<GuestOrder>();
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => ids.Contains(o.PublicId))
            .OrderByDescending(o => o.CreatedUtc)
            .Select(o => new { o.PublicId, o.Number, o.TableNumber, o.Status, o.Total, Count = o.Items.Sum(i => i.Quantity), o.Language })
            .ToListAsync();
        return orders.Select(o => new GuestOrder(o.PublicId, o.Number, o.TableNumber, KitchenOrder.StatusId(o.Status),
            OrderText.Status(o.Status, lang ?? o.Language), o.Total, o.Count)).ToList();
    }

    /// <summary>Open orders plus the last few hours' finished ones, oldest first.</summary>
    public async Task<List<KitchenOrder>> KitchenOrdersAsync(int branchId, DateTime utcNow)
    {
        var recent = utcNow.AddHours(-3);
        var open = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.BranchId == branchId && (o.Status == OrderStatus.New || o.Status == OrderStatus.Preparing))
            .OrderBy(o => o.CreatedUtc).Take(200).ToListAsync();
        var done = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.BranchId == branchId && (o.Status == OrderStatus.Served || o.Status == OrderStatus.Cancelled) && o.UpdatedUtc > recent)
            .OrderByDescending(o => o.UpdatedUtc).Take(30).ToListAsync();
        return open.Concat(done).Select(KitchenOrder.From).ToList();
    }

    /// <summary>
    /// Moves an order from <paramref name="from"/> to <paramref name="to"/> in one atomic
    /// update, so two screens tapping at once can't undo each other: the second gets the
    /// order as it now is and <c>Conflict = true</c>. Null when the order isn't this branch's.
    /// </summary>
    public async Task<(KitchenOrder? Order, bool Conflict)> SetStatusAsync(int branchId, int orderId, OrderStatus from, OrderStatus to, DateTime utcNow)
    {
        var changed = from != to && await _db.Orders
            .Where(o => o.Id == orderId && o.BranchId == branchId && o.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to).SetProperty(o => o.UpdatedUtc, utcNow)) == 1;

        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId && o.BranchId == branchId);
        if (order == null) return (null, false);
        if (changed) await NotifyAsync(order);
        // Cancelling gives the ingredients back; restoring uses them again.
        if (changed && (from == OrderStatus.Cancelled || to == OrderStatus.Cancelled)) await _stock.SyncOrderAsync(orderId, utcNow);
        return (KitchenOrder.From(order), !changed);
    }

    public async Task SetPausedAsync(int branchId, bool paused)
    {
        await _db.Branches.Where(b => b.Id == branchId).ExecuteUpdateAsync(s => s.SetProperty(b => b.OrdersPaused, paused));
        try
        {
            await _hub.Clients.Group(KitchenHub.Group(branchId)).SendAsync("paused", paused);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kitchen push failed for branch {BranchId}", branchId);
        }
    }

    // Pushing is best effort: an order is saved whether or not a screen is listening, and
    // kitchen displays reload the full list whenever they reconnect.
    private async Task NotifyAsync(Order order)
    {
        try
        {
            await _hub.Clients.Group(KitchenHub.Group(order.BranchId)).SendAsync("order", KitchenOrder.From(order));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kitchen push failed for order {OrderId}", order.Id);
        }
    }
}
