using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// The POS (docs/PLAN-backoffice.md, phase 6): staff take dine-in and takeaway orders, see open bills
/// (guest QR orders included), add a discount, take payment and print a pro-forma bill; managers see the
/// day's cash report. Waiters, cashiers and up use it (BranchPermission.Pos); the cash report is
/// BranchPermission.CashReport (cashiers and managers).
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("pos/{id:int}")]
[RequireModule(BillingModule.Ordering)]
public class PosController : Controller
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IBranchAccess _access;
    private readonly OrderService _orders;
    private readonly PosService _pos;
    private readonly UserManager<ApplicationUser> _users;

    public PosController(ApplicationDbContext db, IBranchAccess access, OrderService orders, PosService pos, UserManager<ApplicationUser> users)
    {
        _db = db;
        _access = access;
        _orders = orders;
        _pos = pos;
        _users = users;
    }

    private Task<Branch?> BranchAsync(int id, BranchPermission permission = BranchPermission.Pos)
    {
        var allowed = _access.BranchIds(permission);
        return _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        ViewBag.Currency = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.CanSeeCash = await _access.CanAsync(id, BranchPermission.CashReport);
        return View(branch);
    }

    /// <summary>The menu, tables, discounts and open bills, for the POS screen (pos.js).</summary>
    [HttpGet("data")]
    public async Task<IActionResult> Data(int id)
    {
        if (await BranchAsync(id) == null) return NotFound();
        Response.Headers.CacheControl = "no-store";

        var categories = await _db.Categories.AsNoTracking().Where(c => c.BranchId == id)
            .OrderBy(c => c.Priority).Select(c => new { c.Id, c.Name }).ToListAsync();
        var products = await _db.Products.AsNoTracking().Where(p => p.BranchId == id)
            .Include(p => p.OptionGroups!).ThenInclude(g => g.Options).AsSplitQuery()
            .OrderBy(p => p.DisplayOrder).ToListAsync();
        var tables = await _db.Tables.AsNoTracking().Where(t => t.BranchId == id).OrderBy(t => t.Number)
            .Select(t => new { t.Id, t.Number, t.Name, Area = t.Area != null ? t.Area.Name : null }).ToListAsync();
        var discounts = await _db.Discounts.AsNoTracking().Where(d => d.BranchId == id && d.Enabled)
            .OrderBy(d => d.Name).Select(d => new { d.Id, d.Name }).ToListAsync();

        return Ok(new
        {
            categories = categories.Select(c => new
            {
                c.Id,
                c.Name,
                items = products.Where(p => p.CategoryId == c.Id).Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Price,
                    available = p.IsAvailable,
                    groups = (p.OptionGroups ?? new List<ProductOptionGroup>()).OrderBy(g => g.DisplayOrder).Select(g => new
                    {
                        g.Id, g.Name, min = g.MinSelect, max = g.MaxSelect,
                        options = g.Options.OrderBy(o => o.DisplayOrder).Select(o => new { o.Id, o.Name, delta = o.PriceDelta })
                    })
                })
            }),
            tables,
            discounts,
            bills = await _pos.OpenBillsAsync(id, DateTime.UtcNow)
        });
    }

    [HttpGet("bills")]
    public async Task<IActionResult> Bills(int id)
    {
        if (await BranchAsync(id) == null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { bills = await _pos.OpenBillsAsync(id, DateTime.UtcNow) });
    }

    /// <summary>Sends a new order to the stations. <paramref name="payload"/> is a PosOrderRequest as JSON.</summary>
    [HttpPost("orders")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Place(int id, [FromForm] string? payload)
    {
        if (await BranchAsync(id) == null) return NotFound();
        var request = Parse<PosOrderRequest>(payload);
        if (request == null) return BadRequest(new { message = "That order couldn't be read. Reload and try again." });

        var result = await _orders.PlacePosAsync(id, request, _users.GetUserId(User), DateTime.UtcNow);
        if (result.Order == null) return BadRequest(new { message = result.Message ?? "That order couldn't be sent." });
        return Ok(new { number = result.Order.Number, bills = await _pos.OpenBillsAsync(id, DateTime.UtcNow) });
    }

    [HttpPost("bills/{key}/discount")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discount(int id, string key, [FromForm] int? discountId)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var (bill, problem) = await _pos.SetDiscountAsync(id, key, discountId, _users.GetUserId(User),
            OpeningHours.Zone(branch.TimeZone), DateTime.UtcNow);
        if (bill == null) return NotFound();
        return problem == null ? Ok(new { bill }) : BadRequest(new { message = problem, bill });
    }

    [HttpPost("bills/{key}/request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestBill(int id, string key, [FromForm] bool requested)
    {
        if (await BranchAsync(id) == null) return NotFound();
        var bill = await _pos.RequestBillAsync(id, key, requested, DateTime.UtcNow);
        return bill == null ? NotFound() : Ok(new { bill });
    }

    /// <summary>Takes payment and closes the bill. <paramref name="payload"/>: { tenders: [{ method, amount }], tip }.</summary>
    [HttpPost("bills/{key}/pay")]
    [AllowWhenModuleOff] // bills already open can still be paid
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, string key, [FromForm] string? payload)
    {
        if (await BranchAsync(id) == null) return NotFound();
        var request = Parse<PayRequest>(payload);
        if (request?.Tenders == null) return BadRequest(new { message = "That payment couldn't be read. Reload and try again." });

        var user = await _users.GetUserAsync(User);
        var tenders = request.Tenders.Select(t => new Tender(t.Method, Math.Round(t.Amount, 2))).ToList();
        var (receipt, problem) = await _pos.PayAsync(id, key, tenders, Math.Round(request.Tip, 2), user?.Id,
            user?.FullName ?? user?.Email, DateTime.UtcNow);
        if (receipt == null && problem == null) return NotFound();
        if (receipt == null) return BadRequest(new { message = problem });
        return Ok(new { change = receipt.Change, total = receipt.Bill.Total, bills = await _pos.OpenBillsAsync(id, DateTime.UtcNow) });
    }

    /// <summary>A pro-forma bill to print or show the guest (not a fiscal receipt).</summary>
    [HttpGet("bills/{key}/print")]
    public async Task<IActionResult> Print(int id, string key)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var bill = await _pos.BillAsync(id, key, DateTime.UtcNow);
        if (bill == null) return NotFound();
        ViewBag.Currency = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.LocalNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, OpeningHours.Zone(branch.TimeZone));
        return View("Bill", (branch, bill));
    }

    /// <summary>The day's payments (cashiers, managers and the owner).</summary>
    [HttpGet("cash")]
    [AllowWhenModuleOff]
    public async Task<IActionResult> Cash(int id, string? date)
    {
        var branch = await BranchAsync(id, BranchPermission.CashReport);
        if (branch == null) return NotFound();
        var zone = OpeningHours.Zone(branch.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", out var d) && d <= today ? d : today;
        ViewBag.Currency = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.Branch = branch;
        ViewBag.Today = today;
        return View(await _pos.CashReportAsync(id, day, zone));
    }

    private static T? Parse<T>(string? payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 20_000) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed class PayRequest
    {
        public List<TenderRequest>? Tenders { get; set; }
        public decimal Tip { get; set; }
    }

    public sealed class TenderRequest
    {
        public PaymentMethod Method { get; set; }
        public decimal Amount { get; set; }
    }
}
