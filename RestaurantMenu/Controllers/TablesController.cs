using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Tables &amp; ordering (Manager and up): switch table ordering on or off, set up the
/// tables guests can order from, and give tables new codes when old printouts or
/// links must stop working.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/tables")]
[RequireModule(BillingModule.Ordering)]
public class TablesController : Controller
{
    public const int MaxTables = 500;
    public const int MaxAddAtOnce = 200;

    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly QrCodeService _qr;
    private readonly OrderService _orders;

    public TablesController(ApplicationDbContext context, IBranchAccess access, QrCodeService qr, OrderService orders)
    {
        _context = context;
        _access = access;
        _qr = qr;
        _orders = orders;
    }

    private Task<Branch?> BranchAsync(int id, BranchPermission permission = BranchPermission.EditBranch)
    {
        var allowed = _access.BranchIds(permission);
        return _context.Branches.FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    private IActionResult Back(int id) => RedirectToAction(nameof(Index), new { id });
    private IActionResult BackToFloor(int id) => RedirectToAction(nameof(Floor), new { id });

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();

        var tables = await _context.Tables.AsNoTracking().Include(t => t.Area).Where(t => t.BranchId == id).OrderBy(t => t.Number).ToListAsync();
        var areas = await _context.TableAreas.AsNoTracking().Where(a => a.BranchId == id).OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).ToListAsync();
        var since = DateTime.UtcNow.AddDays(-30);
        ViewBag.OrderCounts = await _context.Orders.AsNoTracking()
            .Where(o => o.BranchId == id && o.CreatedUtc > since && o.TableId != null)
            .GroupBy(o => o.TableId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        ViewBag.Links = tables.ToDictionary(t => t.Id, t => _qr.MenuLink(branch, t.Number, t.Code));
        return View(new TablesPage(branch, tables, areas));
    }

    /// <summary>The live floor plan (waiters, managers and the owner); changing the layout stays on Index.</summary>
    [HttpGet("floor")]
    public async Task<IActionResult> Floor(int id)
    {
        var branch = await BranchAsync(id, BranchPermission.FloorPlan);
        if (branch == null) return NotFound();

        var tables = await _context.Tables.AsNoTracking().Include(t => t.Area)
            .Where(t => t.BranchId == id).OrderBy(t => t.Number).ToListAsync();
        var areas = await _context.TableAreas.AsNoTracking().Where(a => a.BranchId == id)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).ToListAsync();
        var openOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.BranchId == id && o.TableId != null && (o.Status == OrderStatus.New || o.Status == OrderStatus.Preparing))
            .OrderByDescending(o => o.CreatedUtc).ToListAsync();
        var orderByTable = openOrders.GroupBy(o => o.TableId!.Value).ToDictionary(g => g.Key, g => g.First());

        var zone = OpeningHours.Zone(branch.TimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var from = localNow.Date;
        var to = from.AddDays(1).AddHours(6);
        var canSeeBookings = await _access.HasModuleAsync(id, BillingModule.Bookings)
            && await _access.CanAsync(id, BranchPermission.Bookings);
        var reservations = canSeeBookings
            ? await _context.Reservations.AsNoTracking().Where(r => r.BranchId == id && r.StartsAtLocal >= from && r.StartsAtLocal < to
                    && r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow)
                .OrderBy(r => r.StartsAtLocal).ToListAsync()
            : new List<Reservation>();
        var reservationsByTable = reservations.Where(r => r.TableId != null).GroupBy(r => r.TableId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Reservation>)g.ToList());

        var rows = tables.Select(t =>
        {
            orderByTable.TryGetValue(t.Id, out var order);
            reservationsByTable.TryGetValue(t.Id, out var tableReservations);
            tableReservations ??= Array.Empty<Reservation>();
            var status = TableStatusRules.Resolve(order, tableReservations, localNow);
            return new LiveTable(t, status, TableStatusRules.Label(status), order, tableReservations);
        }).ToList();

        ViewBag.CanEditLayout = await _access.CanAsync(id, BranchPermission.EditBranch);
        ViewBag.CanOpenKitchen = await _access.CanAsync(id, BranchPermission.Kitchen);
        return View(new FloorPage(branch, areas, rows, reservations, canSeeBookings, localNow));
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    [AllowWhenModuleOff] // switching ordering off is always allowed; on needs the plan
    public async Task<IActionResult> Settings(int id, bool orderingEnabled)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        if (orderingEnabled && !await _access.HasModuleAsync(id, BillingModule.Ordering))
        {
            TempData["Error"] = "Table ordering isn't in your plan. Contact us to add it.";
            return Back(id);
        }

        branch.OrderingEnabled = orderingEnabled;
        if (!orderingEnabled) branch.OrdersPaused = false; // switching back on starts fresh
        await _context.SaveChangesAsync();
        if (!orderingEnabled) await _orders.SetPausedAsync(id, false);

        var tableCount = await _context.Tables.CountAsync(t => t.BranchId == id);
        TempData["Success"] = orderingEnabled
            ? tableCount == 0
                ? "Table ordering is on. Add your tables below, then print their QR codes."
                : "Table ordering is on. Guests scanning a table's QR code can now send their list to the kitchen."
            : "Table ordering is off. Guests can still make a list and show it to their server.";
        return Back(id);
    }

    [HttpPost("add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int id, int from, int? to, string? name, int? areaId)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();

        var last = to ?? from;
        if (SeoService.ValidTable(from) == null || SeoService.ValidTable(last) == null)
        {
            TempData["Error"] = $"Table numbers go from 1 to {SeoService.MaxTable}.";
            return Back(id);
        }
        if (last < from) (from, last) = (last, from);
        if (last - from + 1 > MaxAddAtOnce)
        {
            TempData["Error"] = $"Add up to {MaxAddAtOnce} tables at a time.";
            return Back(id);
        }
        name = CleanName(name);
        if (areaId != null && !await _context.TableAreas.AnyAsync(a => a.Id == areaId && a.BranchId == id))
        {
            TempData["Error"] = "Choose an area from this branch.";
            return Back(id);
        }

        var existing = await _context.Tables.Where(t => t.BranchId == id).Select(t => t.Number).ToListAsync();
        var numbers = Enumerable.Range(from, last - from + 1).Where(n => !existing.Contains(n)).ToList();
        if (numbers.Count == 0)
        {
            TempData["Warning"] = from == last ? $"Table {from} is already set up." : $"Tables {from} to {last} are already set up.";
            return Back(id);
        }
        if (existing.Count + numbers.Count > MaxTables)
        {
            TempData["Error"] = $"A branch can have up to {MaxTables} tables.";
            return Back(id);
        }

        var now = DateTime.UtcNow;
        var offset = existing.Count;
        _context.Tables.AddRange(numbers.Select((n, index) => new DiningTable
        {
            BranchId = id, Number = n, Name = name, AreaId = areaId, Code = OrderRules.NewCode(), CreatedUtc = now,
            X = 8 + ((offset + index) % 5) * 18, Y = 8 + ((offset + index) / 5 % 5) * 18
        }));
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Someone added some of the same numbers at the same moment.
            TempData["Error"] = "Some of those tables were added meanwhile. Check the list and try again.";
            return Back(id);
        }

        var skipped = last - from + 1 - numbers.Count;
        TempData["Success"] = $"{BranchController.TableRanges(numbers)} added" + (skipped > 0 ? $" ({skipped} already existed)." : ".")
                              + " Print their QR codes so guests can order.";
        TempData["PrintFrom"] = numbers.Min();
        TempData["PrintTo"] = numbers.Max();
        return Back(id);
    }

    [HttpPost("areas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddArea(int id, string? name)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        name = CleanAreaName(name);
        if (name == null)
        {
            TempData["Error"] = "Enter an area name.";
            return Back(id);
        }
        if (await _context.TableAreas.AnyAsync(a => a.BranchId == id && a.Name.ToLower() == name.ToLower()))
        {
            TempData["Warning"] = $"{name} already exists.";
            return Back(id);
        }
        var displayOrder = await _context.TableAreas.Where(a => a.BranchId == id).Select(a => (int?)a.DisplayOrder).MaxAsync() ?? -1;
        _context.TableAreas.Add(new TableArea { BranchId = id, Name = name, DisplayOrder = displayOrder + 1 });
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{name} added.";
        return Back(id);
    }

    [HttpPost("areas/{areaId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteArea(int id, int areaId)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var area = await _context.TableAreas.FirstOrDefaultAsync(a => a.Id == areaId && a.BranchId == id);
        if (area == null) return NotFound();
        await _context.Tables.Where(t => t.BranchId == id && t.AreaId == areaId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AreaId, (int?)null));
        _context.TableAreas.Remove(area);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{area.Name} removed. Its tables are now unassigned.";
        return Back(id);
    }

    [HttpPost("{tableId:int}/layout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Layout(int id, int tableId, int? areaId, TableShape shape, int seats, decimal? x, decimal? y, int rotation)
    {
        var table = await TableAsync(id, tableId);
        if (table == null) return NotFound();
        // The plan above the list places tables by dragging; this form only changes them.
        x ??= table.X;
        y ??= table.Y;
        if (!TableLayoutRules.IsValid(shape, seats, x.Value, y.Value, rotation))
        {
            TempData["Error"] = "Use 1–50 seats, a position from 0–100, and a valid rotation.";
            return Back(id);
        }
        if (areaId != null && !await _context.TableAreas.AnyAsync(a => a.Id == areaId && a.BranchId == id)) return BadRequest();
        table.AreaId = areaId;
        table.Shape = shape;
        table.Seats = seats;
        table.X = x.Value;
        table.Y = y.Value;
        table.Rotation = rotation;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Table {table.Number}'s layout saved.";
        return Back(id);
    }

    /// <summary>
    /// A table dragged on the plan (floor-editor.js): its new spot, snapped to the grid, and its area
    /// (<paramref name="areaId"/> empty for Unassigned). Answers with where it was saved.
    /// </summary>
    [HttpPost("{tableId:int}/position")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Position(int id, int tableId, decimal x, decimal y, int? areaId)
    {
        var table = await TableAsync(id, tableId);
        if (table == null) return NotFound();
        if (areaId != null && !await _context.TableAreas.AnyAsync(a => a.Id == areaId && a.BranchId == id))
            return BadRequest(new { message = "That area no longer exists. Reload the page." });

        table.X = TableLayoutRules.Snap(x);
        table.Y = TableLayoutRules.Snap(y);
        table.AreaId = areaId;
        await _context.SaveChangesAsync();
        return Ok(new { x = table.X, y = table.Y, areaId = table.AreaId });
    }

    [HttpPost("floor/reservations/{reservationId:int}/seat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeatReservation(int id, int reservationId, int tableId)
    {
        if (await BranchAsync(id, BranchPermission.FloorPlan) == null || !await _access.HasModuleAsync(id, BillingModule.Bookings)
            || !await _access.CanAsync(id, BranchPermission.Bookings)) return NotFound();
        var table = await _context.Tables.FirstOrDefaultAsync(t => t.Id == tableId && t.BranchId == id);
        var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId && r.BranchId == id);
        if (table == null || reservation == null) return NotFound();
        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.NoShow) return BadRequest();
        reservation.TableId = table.Id;
        reservation.Status = ReservationStatus.Seated;
        reservation.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{reservation.Name} seated at table {table.Number}.";
        return BackToFloor(id);
    }

    [HttpPost("{tableId:int}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, int tableId, string? name)
    {
        var table = await TableAsync(id, tableId);
        if (table == null) return NotFound();
        table.Name = CleanName(name);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Table {table.Number} saved.";
        return Back(id);
    }

    [HttpPost("{tableId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int tableId)
    {
        var table = await TableAsync(id, tableId);
        if (table == null) return NotFound();
        _context.Tables.Remove(table);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Table {table.Number} removed. Its QR code now opens the menu without ordering. Past orders keep its number.";
        return Back(id);
    }

    [HttpPost("{tableId:int}/newcode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewCode(int id, int tableId)
    {
        var table = await TableAsync(id, tableId);
        if (table == null) return NotFound();
        table.Code = OrderRules.NewCode();
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Table {table.Number} has a new code. Its old QR code can't send orders any more, so print a new one.";
        TempData["PrintFrom"] = TempData["PrintTo"] = table.Number;
        return Back(id);
    }

    [HttpPost("newcodes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewCodes(int id)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var tables = await _context.Tables.Where(t => t.BranchId == id).ToListAsync();
        foreach (var t in tables) t.Code = OrderRules.NewCode();
        await _context.SaveChangesAsync();
        TempData["Success"] = $"All {tables.Count} tables have new codes. Old QR codes can't send orders any more, so print new ones.";
        return Back(id);
    }

    private async Task<DiningTable?> TableAsync(int id, int tableId)
    {
        if (await BranchAsync(id) == null) return null;
        return await _context.Tables.FirstOrDefaultAsync(t => t.Id == tableId && t.BranchId == id);
    }

    private static string? CleanName(string? name)
    {
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name.Length > 40 ? name[..40] : name;
    }

    private static string? CleanAreaName(string? name)
    {
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name.Length > 60 ? name[..60] : name;
    }
}

public record TablesPage(Branch Branch, IReadOnlyList<DiningTable> Tables, IReadOnlyList<TableArea> Areas);
public record LiveTable(DiningTable Table, LiveTableStatus Status, string StatusLabel, Order? Order, IReadOnlyList<Reservation> Reservations);
public record FloorPage(Branch Branch, IReadOnlyList<TableArea> Areas, IReadOnlyList<LiveTable> Tables,
    IReadOnlyList<Reservation> Reservations, bool CanSeeBookings, DateTime LocalNow);
