using System.Globalization;
using System.Text;
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
/// End-of-day sales for a branch (Manager and up): table orders per day, the period against
/// the one before, top dishes, and the closing count of cash and card. Exports as CSV.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/sales")]
public class SalesController : Controller
{
    public static readonly int[] Ranges = { 7, 30, 90 };

    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly SalesService _sales;
    private readonly UserManager<ApplicationUser> _users;

    public SalesController(ApplicationDbContext context, IBranchAccess access, SalesService sales, UserManager<ApplicationUser> users)
    {
        _context = context;
        _access = access;
        _sales = sales;
        _users = users;
    }

    private Task<Branch?> BranchAsync(int id)
    {
        var allowed = _access.BranchIds(BranchPermission.ViewInsights);
        return _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    private static DateOnly Today(Branch b) => BookingRules.Today(OpeningHours.Zone(b.TimeZone), DateTime.UtcNow);

    [HttpGet("")]
    public async Task<IActionResult> Index(int id, int days = 30, string? close = null)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        days = Ranges.Contains(days) ? days : 30;
        var today = Today(branch);
        var from = today.AddDays(-(days - 1));
        var current = await _sales.DaysAsync(id, from, today, today);
        var previous = await _sales.DaysAsync(id, from.AddDays(-days), from.AddDays(-1), today);
        var closeDay = DateOnly.TryParseExact(close, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var c) && c <= today && c >= today.AddDays(-90) ? c : today;
        var closing = current.FirstOrDefault(d => d.Date == closeDay) ?? await DayAsync(id, closeDay, today);

        // Top dishes of the period, from the orders themselves.
        var top = await _context.OrderItems.AsNoTracking()
            .Where(i => i.Order!.BranchId == id && i.Order.OrderDay >= from && i.Order.OrderDay <= today && i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.Name)
            .Select(g => new { Name = g.Key, Qty = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.UnitPrice * i.Quantity) })
            .OrderByDescending(t => t.Revenue).Take(8).ToListAsync();

        return View(new SalesPage(branch, days, today, current, previous, top.Select(t => new TopDish(t.Name, t.Qty, t.Revenue)).ToList(), closing, CurrencyHelper.GetCurrencySymbol(branch.Currency)));
    }

    private async Task<DaySales> DayAsync(int id, DateOnly day, DateOnly today) => (await _sales.DaysAsync(id, day, day, today))[0];

    /// <summary>The closing count for a day: cash and card taken (from the till), with a note.</summary>
    [HttpPost("close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id, string? date, string? cash, string? card, string? note, int days = 30)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var today = Today(branch);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || day > today || day < today.AddDays(-90))
        {
            TempData["Error"] = "Choose a day from the last 90 days.";
            return RedirectToAction(nameof(Index), new { id, days });
        }
        decimal? Money(string? s) => string.IsNullOrWhiteSpace(s) ? null : StockRules.Parse(s) is { } v && decimal.Round(v, 2) == v ? v : -1;
        var c = Money(cash);
        var k = Money(card);
        if (c == -1 || k == -1 || (c == null && k == null))
        {
            TempData["Error"] = "Enter the cash and/or card totals as amounts, like 1250 or 1250.50.";
            return RedirectToAction(nameof(Index), new { id, days, close = date });
        }
        var user = await _users.GetUserAsync(User);
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim().Length > 300 ? note.Trim()[..300] : note.Trim();
        var row = await _sales.CloseDayAsync(id, day, c, k, cleanNote, user?.FullName ?? user?.Email, DateTime.UtcNow);
        var symbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        var counted = (row.CashTotal ?? 0) + (row.CardTotal ?? 0);
        var en = CultureInfo.GetCultureInfo("en-GB"); // the back office's format, whatever the server's culture
        TempData["Success"] = $"{day.ToString("ddd d MMM", en)} closed: {symbol}{counted.ToString("N2", en)} counted, {symbol}{row.Revenue.ToString("N2", en)} from table orders.";
        return RedirectToAction(nameof(Index), new { id, days });
    }

    /// <summary>The period as CSV, for the accountant (one row per day).</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> Export(int id, int days = 30)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        days = Ranges.Contains(days) ? days : 30;
        var today = Today(branch);
        var list = await _sales.DaysAsync(id, today.AddDays(-(days - 1)), today, today);
        var inv = CultureInfo.InvariantCulture;
        static string Q(string? s) => s == null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Date,Orders,Items,Revenue,Cancelled orders,Cash counted,Card counted,Closed by,Note\r\n");
        foreach (var d in list)
            sb.Append($"{d.Date:yyyy-MM-dd},{d.Orders},{d.Items},{d.Revenue.ToString("0.00", inv)},{d.Cancelled},{d.Cash?.ToString("0.00", inv)},{d.Card?.ToString("0.00", inv)},{Q(d.ClosedBy)},{Q(d.CloseNote)}\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv; charset=utf-8",
            $"{SeoService.Slug(branch.Name)}-sales-{today:yyyy-MM-dd}.csv");
    }
}

public record SalesPage(Branch Branch, int Days, DateOnly Today, IReadOnlyList<DaySales> Current, IReadOnlyList<DaySales> Previous,
    IReadOnlyList<TopDish> Top, DaySales Closing, string Currency);
