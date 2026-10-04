using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Overview across branches (/manage): sales today, yesterday, 7 and 30 days, low stock and
/// who's on shift now, for every branch the person manages (Manager and up), with totals.
/// Branches with different currencies are never added together.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("manage")]
public class ManageController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly SalesService _sales;

    public ManageController(ApplicationDbContext context, IBranchAccess access, SalesService sales)
    {
        _context = context;
        _access = access;
        _sales = sales;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var branches = await _access.Branches(BranchPermission.ViewInsights).AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        var rows = new List<BranchOverview>();
        foreach (var b in branches)
        {
            var zone = OpeningHours.Zone(b.TimeZone);
            var today = BookingRules.Today(zone, DateTime.UtcNow);
            var days = await _sales.DaysAsync(b.Id, today.AddDays(-29), today, today);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            var onShift = await _context.Shifts.AsNoTracking()
                .Where(s => s.BranchId == b.Id && s.StartsLocal <= nowLocal && s.EndsLocal > nowLocal)
                .OrderBy(s => s.PersonName).Select(s => s.PersonName).ToListAsync();
            var low = await _context.Ingredients.AsNoTracking()
                .Where(i => i.BranchId == b.Id && !i.IsArchived && (i.Quantity <= 0 || (i.LowLevel > 0 && i.Quantity <= i.LowLevel)))
                .OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
            rows.Add(new BranchOverview(b, b.Currency, CurrencyHelper.GetCurrencySymbol(b.Currency),
                days[^1], days[^2], days.TakeLast(7).Sum(d => d.Revenue), days.Sum(d => d.Revenue), days.Sum(d => d.Orders), low, onShift,
                days.Count(d => d.Date < today && d.Orders > 0 && d.ClosedUtc == null && d.Date >= today.AddDays(-7))));
        }
        return View(rows);
    }
}

/// <param name="Unclosed">Days in the last week with orders but no closing count.</param>
public record BranchOverview(Branch Branch, string CurrencyCode, string Currency, DaySales Today, DaySales Yesterday,
    decimal Last7, decimal Last30, int Orders30, IReadOnlyList<string> LowStock, IReadOnlyList<string> OnShift, int Unclosed);
