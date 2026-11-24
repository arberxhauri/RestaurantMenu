using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// The kitchen display (/kitchen/{branch}): table orders as they arrive, moved from New
/// to Preparing to Served, and the same for the bar (/kitchen/{branch}/bar). Each screen needs
/// its own station's permission (BranchPermission.Kitchen or .Bar), held by a person or by a
/// paired station device (StationAuth). The page gets live updates from KitchenHub and makes
/// every change here, so the server stays the referee.
/// </summary>
[Authorize(Policy = StationAuth.Policy)]
[NoIndex]
[Route("kitchen/{id:int}")]
[RequireModule(BillingModule.Ordering)]
public class KitchenController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly OrderService _orders;

    public KitchenController(ApplicationDbContext context, IBranchAccess access, OrderService orders)
    {
        _context = context;
        _access = access;
        _orders = orders;
    }

    private Task<Branch?> BranchAsync(int id, ServiceStation station)
    {
        var allowed = _access.BranchIds(StationRules.Permission(station));
        return _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    [HttpGet("")]
    public Task<IActionResult> Index(int id) => StationAsync(id, ServiceStation.Kitchen);

    /// <summary>The bar's screen: drinks and anything else assigned to the bar (Product/Category station).</summary>
    [HttpGet("bar")]
    public Task<IActionResult> Bar(int id) => StationAsync(id, ServiceStation.Bar);

    private async Task<IActionResult> StationAsync(int id, ServiceStation station)
    {
        var branch = await BranchAsync(id, station);
        if (branch == null) return NotFound();
        // A shared tablet signed in as a station device: no way back into the back office from here.
        ViewBag.DeviceName = !User.IsInRole("OWNER") && !User.IsInRole("STAFF") && StationAuth.Read(User) != null
            ? User.Identity?.Name
            : null;
        ViewBag.Currency = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.CanManage = await _access.CanAsync(id, BranchPermission.EditBranch);
        ViewBag.TableCount = await _context.Tables.CountAsync(t => t.BranchId == id);
        ViewBag.Station = station;
        return View("Index", branch);
    }

    /// <summary>Everything a station's screen shows; fetched on load, after every reconnect, and every 30 seconds.</summary>
    [HttpGet("orders")]
    public async Task<IActionResult> Orders(int id, string? station)
    {
        if (StationRules.ParseStation(station) is not { } s) return BadRequest();
        var branch = await BranchAsync(id, s);
        if (branch == null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            enabled = branch.OrderingEnabled,
            paused = branch.OrdersPaused,
            now = DateTime.UtcNow,
            orders = await _orders.KitchenOrdersAsync(id, s, DateTime.UtcNow)
        });
    }

    /// <summary>
    /// Moves dishes of a station's part of an order on: those in <paramref name="items"/> (comma-separated
    /// ids, a single dish dragged or tapped), or with none every dish in the <paramref name="from"/> column
    /// (the whole card). <paramref name="from"/> is the column the screen saw: if another screen got there
    /// first, 409 returns the order as it is now.
    /// </summary>
    [HttpPost("orders/{orderId:int}/status")]
    [AllowWhenModuleOff] // orders already sent can still be finished
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int id, int orderId, [FromForm] string from, [FromForm] string to, [FromForm] string? station,
        [FromForm] string? items)
    {
        if (StationRules.FromScreen(from) is not { } f || StationRules.FromScreen(to) is not { } t
            || StationRules.ParseStation(station) is not { } s)
        {
            return BadRequest();
        }
        if (StationRules.ParseItemIds(items) is not { } itemIds) return BadRequest();
        if (await BranchAsync(id, s) == null) return NotFound();

        var (order, conflict) = await _orders.SetStationStatusAsync(id, orderId, s, f, t, itemIds, DateTime.UtcNow);
        if (order == null) return NotFound();
        return conflict ? Conflict(new { order }) : Ok(new { order });
    }

    /// <summary>Pauses guest ordering for the whole branch: from the kitchen's or the bar's screen.</summary>
    [HttpPost("pause")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(int id, [FromForm] bool paused)
    {
        if (await BranchAsync(id, ServiceStation.Kitchen) == null && await BranchAsync(id, ServiceStation.Bar) == null) return NotFound();
        await _orders.SetPausedAsync(id, paused);
        return Ok(new { paused });
    }
}
