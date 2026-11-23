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
            var hasOrdering = await _access.HasModuleAsync(b.Id, BillingModule.Ordering);
            var hasBookings = await _access.HasModuleAsync(b.Id, BillingModule.Bookings);
            var hasManagement = await _access.HasModuleAsync(b.Id, BillingModule.Management);
            var zone = OpeningHours.Zone(b.TimeZone);
            var today = BookingRules.Today(zone, DateTime.UtcNow);
            var days = await _sales.DaysAsync(b.Id, today.AddDays(-29), today, today);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            var dayStart = today.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var onShift = await _context.Shifts.AsNoTracking()
                .Where(s => s.BranchId == b.Id && s.StartsLocal <= nowLocal && s.EndsLocal > nowLocal)
                .OrderBy(s => s.PersonName).Select(s => s.PersonName).ToListAsync();
            var low = await _context.Ingredients.AsNoTracking()
                .Where(i => i.BranchId == b.Id && !i.IsArchived && (i.Quantity <= 0 || (i.LowLevel > 0 && i.Quantity <= i.LowLevel)))
                .OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
            var liveOrders = await _context.Orders.AsNoTracking()
                .Where(o => o.BranchId == b.Id && o.OrderDay == today &&
                            (o.Status == OrderStatus.New || o.Status == OrderStatus.Preparing))
                .Select(o => new { o.TableId, o.CreatedUtc }).ToListAsync();
            var bookings = await _context.Reservations.AsNoTracking()
                .Where(r => r.BranchId == b.Id && r.StartsAtLocal >= dayStart && r.StartsAtLocal < dayEnd &&
                            r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow)
                .Select(r => new { r.Guests, r.Status }).ToListAsync();
            var unavailable = await _context.Products.AsNoTracking()
                .CountAsync(p => p.BranchId == b.Id && !p.IsAvailable);
            var lowRatings = await _context.Feedback.AsNoTracking()
                .CountAsync(f => f.BranchId == b.Id && f.Rating <= 2 && f.CreatedUtc >= DateTime.UtcNow.AddDays(-1));
            rows.Add(new BranchOverview(b, b.Currency, CurrencyHelper.GetCurrencySymbol(b.Currency),
                days[^1], days[^2], days[^8], days.TakeLast(7).Sum(d => d.Revenue), days.Sum(d => d.Revenue),
                days.Sum(d => d.Orders), days.TakeLast(14).ToList(), low, onShift,
                days.Count(d => d.Date < today && d.Orders > 0 && d.ClosedUtc == null && d.Date >= today.AddDays(-7)),
                liveOrders.Where(o => o.TableId != null).Select(o => o.TableId).Distinct().Count(),
                liveOrders.Count, liveOrders.Count == 0 ? null : DateTime.UtcNow - liveOrders.Min(o => o.CreatedUtc),
                bookings.Count, bookings.Sum(r => r.Guests), unavailable, lowRatings,
                hasOrdering, hasBookings, hasManagement));
        }
        return View(rows);
    }
}

/// <param name="Unclosed">Days in the last week with orders but no closing count.</param>
public record BranchOverview(Branch Branch, string CurrencyCode, string Currency, DaySales Today, DaySales Yesterday,
    DaySales SameWeekdayLastWeek, decimal Last7, decimal Last30, int Orders30, IReadOnlyList<DaySales> Last14,
    IReadOnlyList<string> LowStock, IReadOnlyList<string> OnShift, int Unclosed, int TablesInUse, int LiveOrders,
    TimeSpan? OldestTicket, int BookingsToday, int CoversToday, int UnavailableItems, int LowRatings,
    bool HasOrdering, bool HasBookings, bool HasManagement);
