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

    private Task<Branch?> BranchAsync(int id)
    {
        var allowed = _access.BranchIds(BranchPermission.EditBranch);
        return _context.Branches.FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    private IActionResult Back(int id) => RedirectToAction(nameof(Index), new { id });

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();

        var tables = await _context.Tables.AsNoTracking().Where(t => t.BranchId == id).OrderBy(t => t.Number).ToListAsync();
        var since = DateTime.UtcNow.AddDays(-30);
        ViewBag.OrderCounts = await _context.Orders.AsNoTracking()
            .Where(o => o.BranchId == id && o.CreatedUtc > since && o.TableId != null)
            .GroupBy(o => o.TableId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        ViewBag.Links = tables.ToDictionary(t => t.Id, t => _qr.MenuLink(branch, t.Number, t.Code));
        return View(new TablesPage(branch, tables));
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(int id, bool orderingEnabled)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();

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
    public async Task<IActionResult> Add(int id, int from, int? to, string? name)
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
        _context.Tables.AddRange(numbers.Select(n => new DiningTable { BranchId = id, Number = n, Name = name, Code = OrderRules.NewCode(), CreatedUtc = now }));
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
}

public record TablesPage(Branch Branch, IReadOnlyList<DiningTable> Tables);
