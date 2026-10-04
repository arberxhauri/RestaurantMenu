using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Bookings in the back office: the day view (Editor and up: confirm, seat, cancel, mark
/// no-shows, add phone bookings and walk-ins, close a day) and the settings (Manager and up).
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/bookings")]
public class BookingsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly BookingService _bookings;
    private readonly SeoService _seo;
    private readonly QrCodeService _qr;

    public BookingsController(ApplicationDbContext context, IBranchAccess access, BookingService bookings, SeoService seo, QrCodeService qr)
    {
        _context = context;
        _access = access;
        _bookings = bookings;
        _seo = seo;
        _qr = qr;
    }

    private Task<Branch?> BranchAsync(int id, BranchPermission permission)
    {
        var allowed = _access.BranchIds(permission);
        return _context.Branches.AsNoTracking().Include(b => b.OpeningHours).FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private IActionResult BackToDay(int id, DateOnly date, string? anchor = null) =>
        Redirect(Url.Action(nameof(Index), new { id, date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }) + (anchor == null ? "" : "#" + anchor));

    public string BookingUrl(Branch b) => _seo.Url("/book/" + SeoService.Slug(b.Name));

    // ---------------------------------------------------------------- day view

    [HttpGet("")]
    public async Task<IActionResult> Index(int id, string? date)
    {
        var branch = await BranchAsync(id, BranchPermission.Bookings);
        if (branch == null) return NotFound();
        var settings = await _bookings.SettingsAsync(id);
        var zone = OpeningHours.Zone(branch.TimeZone);
        var now = DateTime.UtcNow;
        var today = BookingRules.Today(zone, now);
        var day = ParseDate(date) ?? today;

        var from = day.ToDateTime(TimeOnly.MinValue);
        // The day's service runs until 06:00 the next morning (late evenings past midnight).
        var to = from.AddDays(1).AddHours(6);
        var list = await _context.Reservations.AsNoTracking()
            .Where(r => r.BranchId == id && r.StartsAtLocal >= from && r.StartsAtLocal < to)
            .OrderBy(r => r.StartsAtLocal).ThenBy(r => r.CreatedUtc)
            .ToListAsync();
        // Bookings after midnight that belong to the next day's own opening aren't this day's.
        var nextDaySlots = BookingRules.Slots(settings, branch.OpeningHours ?? new List<BranchHours>(), zone, day.AddDays(1), now, new Dictionary<DateTime, int>())
            .Select(s => s.Local).ToHashSet();
        list = list.Where(r => r.StartsAtLocal < from.AddDays(1) || !nextDaySlots.Contains(r.StartsAtLocal)).ToList();

        var slots = BookingRules.Slots(settings, branch.OpeningHours ?? new List<BranchHours>(), zone, day, now,
            list.Where(r => BookingRules.HoldsSeats(r.Status)).GroupBy(r => r.StartsAtLocal).ToDictionary(g => g.Key, g => g.Sum(r => r.Guests)));

        ViewBag.History = await _bookings.HistoryAsync(id, list.Select(r => r.Phone), now);
        ViewBag.CanSettings = await _access.CanAsync(id, BranchPermission.EditBranch);
        ViewBag.BookingUrl = BookingUrl(branch);
        return View(new BookingDay(branch, settings, day, today, list, slots, BookingRules.ClosedDates(settings.ClosedDates).Contains(day),
            BookingService.IsBookable(branch, settings), await StampAsync(id, day)));
    }

    /// <summary>Changes the day view polls for: when it differs, the page offers to refresh.</summary>
    [HttpGet("stamp")]
    public async Task<IActionResult> Stamp(int id, string? date)
    {
        var branch = await BranchAsync(id, BranchPermission.Bookings);
        var day = ParseDate(date);
        if (branch == null || day == null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { stamp = await StampAsync(id, day.Value) });
    }

    private async Task<string> StampAsync(int branchId, DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1).AddHours(6);
        var q = _context.Reservations.Where(r => r.BranchId == branchId && r.StartsAtLocal >= from && r.StartsAtLocal < to);
        var count = await q.CountAsync();
        var last = await q.MaxAsync(r => (DateTime?)r.UpdatedUtc);
        return $"{count}-{last?.Ticks ?? 0}";
    }

    [HttpPost("{reservationId:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int id, int reservationId, string from, string to, string? date)
    {
        var branch = await BranchAsync(id, BranchPermission.Bookings);
        if (branch == null) return NotFound();
        if (!Enum.TryParse<ReservationStatus>(from, true, out var f) || !Enum.TryParse<ReservationStatus>(to, true, out var t)
            || !Enum.IsDefined(f) || !Enum.IsDefined(t) || int.TryParse(from, out _) || int.TryParse(to, out _))
        {
            return BadRequest();
        }
        var (r, conflict) = await _bookings.SetStatusAsync(branch, reservationId, f, t, DateTime.UtcNow);
        if (r == null) return NotFound();
        if (conflict)
        {
            TempData["Warning"] = $"{r.Name}'s booking had already been changed by someone else. Here's how it is now.";
        }
        else
        {
            TempData["Success"] = t switch
            {
                ReservationStatus.Confirmed when f == ReservationStatus.Pending => $"{r.Name}'s booking is confirmed" + (r.Source == "online" ? " and they've been told." : "."),
                ReservationStatus.Seated => $"{r.Name} ({r.Guests}) seated.",
                ReservationStatus.NoShow => $"{r.Name} marked as a no-show.",
                ReservationStatus.Cancelled => $"{r.Name}'s booking is cancelled" + (r.Source == "online" && r.StartsAtUtc > DateTime.UtcNow ? " and they've been told." : "."),
                _ => $"{r.Name}'s booking is {t.ToString().ToLowerInvariant()} again."
            };
        }
        return BackToDay(id, ParseDate(date) ?? DateOnly.FromDateTime(r.StartsAtLocal), $"r{r.Id}");
    }

    /// <summary>A booking taken by phone, or a walk-in. Name and time required; may overbook.</summary>
    [HttpPost("add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int id, string? date, string? time, int guests, string? name, string? phone, string? note)
    {
        var branch = await BranchAsync(id, BranchPermission.Bookings);
        if (branch == null) return NotFound();
        var settings = await _bookings.SettingsAsync(id);
        var day = ParseDate(date);
        if (day == null || !TimeOnly.TryParseExact(time, new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            TempData["Error"] = "Choose a date and a time like 19:30.";
            return BackToDay(id, day ?? BookingRules.Today(OpeningHours.Zone(branch.TimeZone), DateTime.UtcNow), "add");
        }
        var cleanName = BookingRules.Clean(name, BookingRules.MaxNameLength);
        if (cleanName == null || guests < 1 || guests > 500)
        {
            TempData["Error"] = cleanName == null ? "Enter the guest's name." : "Guests must be between 1 and 500.";
            return BackToDay(id, day.Value, "add");
        }
        string? cleanPhone = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            cleanPhone = BookingRules.NormalizePhone(phone, settings.CountryCode);
            if (cleanPhone == null)
            {
                TempData["Error"] = "That phone number doesn't look right. Leave it empty for a walk-in.";
                return BackToDay(id, day.Value, "add");
            }
        }
        // Times after midnight but before 06:00 belong to the evening of the chosen day.
        var local = day.Value.ToDateTime(t);
        if (t < new TimeOnly(6, 0) && (branch.OpeningHours ?? new List<BranchHours>()).Any(h => h.DayOfWeek == day.Value.DayOfWeek && h.Closes <= h.Opens && h.Closes > t))
            local = local.AddDays(1);
        var r = await _bookings.AddByStaffAsync(branch, local, guests, cleanName, cleanPhone, BookingRules.Clean(note, BookingRules.MaxNoteLength), DateTime.UtcNow);

        var held = await _context.Reservations.Where(x => x.BranchId == id && x.StartsAtLocal == r.StartsAtLocal
            && (x.Status == ReservationStatus.Pending || x.Status == ReservationStatus.Confirmed || x.Status == ReservationStatus.Seated)).SumAsync(x => x.Guests);
        TempData["Success"] = $"{r.Name} ({r.Guests}) booked for {r.StartsAtLocal:HH:mm}.";
        if (held > settings.CoversPerSlot) TempData["Warning"] = $"{r.StartsAtLocal:HH:mm} now has {held} guests, more than the {settings.CoversPerSlot} you take per slot.";
        return BackToDay(id, day.Value, $"r{r.Id}");
    }

    /// <summary>Close or reopen one day for online bookings (existing bookings stay).</summary>
    [HttpPost("closeday")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseDay(int id, string? date, bool closed)
    {
        var branch = await BranchAsync(id, BranchPermission.Bookings);
        var day = ParseDate(date);
        if (branch == null || day == null) return NotFound();
        var settings = await EnsureSettingsAsync(id);
        var today = BookingRules.Today(OpeningHours.Zone(branch.TimeZone), DateTime.UtcNow);
        var dates = BookingRules.ClosedDates(settings.ClosedDates);
        if (closed) dates.Add(day.Value); else dates.Remove(day.Value);
        settings.ClosedDates = BookingRules.StoreClosedDates(dates, today);
        await _context.SaveChangesAsync();
        TempData["Success"] = closed
            ? $"{day.Value.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("en-GB"))} is closed for online bookings. Bookings already made stay."
            : "This day is open for online bookings again.";
        return BackToDay(id, day.Value);
    }

    private async Task<ReservationSettings> EnsureSettingsAsync(int id)
    {
        var s = await _context.ReservationSettings.FirstOrDefaultAsync(x => x.BranchId == id);
        if (s == null)
        {
            s = new ReservationSettings { BranchId = id };
            _context.ReservationSettings.Add(s);
        }
        return s;
    }

    // ---------------------------------------------------------------- settings

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(int id)
    {
        var branch = await BranchAsync(id, BranchPermission.EditBranch);
        if (branch == null) return NotFound();
        ViewBag.BookingUrl = BookingUrl(branch);
        ViewBag.QrSvg = _qr.Svg(BookingUrl(branch));
        return View(new BookingSettingsPage(branch, await _bookings.SettingsAsync(id), null));
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(int id, ReservationSettings form)
    {
        var branch = await BranchAsync(id, BranchPermission.EditBranch);
        if (branch == null) return NotFound();

        var errors = new List<string>();
        if (!BookingRules.SlotLengths.Contains(form.SlotMinutes)) errors.Add("Choose 15, 30 or 60 minutes between times.");
        if (form.CoversPerSlot is < 1 or > 1000) errors.Add("Guests per time must be between 1 and 1000.");
        if (form.MaxPartySize is < 1 or > 100) errors.Add("The largest party must be between 1 and 100.");
        if (form.MaxPartySize > form.CoversPerSlot) errors.Add("The largest party can't be bigger than the guests you take per time.");
        if (form.LeadMinutes is < 0 or > 10080) errors.Add("Notice must be between 0 minutes and 7 days.");
        if (form.MaxDaysAhead is < 1 or > 365) errors.Add("Bookings can open between 1 and 365 days ahead.");
        if (form.LastSeatingMinutes is < 0 or > 600) errors.Add("Last booking time must be between 0 and 600 minutes before closing.");
        if (form.ReminderHours is < 0 or > 48) errors.Add("Reminders can go out up to 48 hours before.");
        var cc = new string((form.CountryCode ?? "").Where(char.IsAsciiDigit).ToArray());
        if (cc.Length is < 1 or > 3) errors.Add("The country code is 1 to 3 digits, e.g. 355 for Albania.");

        var s = await EnsureSettingsAsync(id);
        if (errors.Count > 0)
        {
            ViewBag.BookingUrl = BookingUrl(branch);
            ViewBag.QrSvg = _qr.Svg(BookingUrl(branch));
            form.BranchId = id;
            form.ClosedDates = s.ClosedDates;
            return View(new BookingSettingsPage(branch, form, errors));
        }

        s.Enabled = form.Enabled;
        s.SlotMinutes = form.SlotMinutes;
        s.CoversPerSlot = form.CoversPerSlot;
        s.MaxPartySize = form.MaxPartySize;
        s.LeadMinutes = form.LeadMinutes;
        s.MaxDaysAhead = form.MaxDaysAhead;
        s.LastSeatingMinutes = form.LastSeatingMinutes;
        s.AutoConfirm = form.AutoConfirm;
        s.ReminderHours = form.ReminderHours;
        s.CountryCode = cc;
        s.NotifyOwner = form.NotifyOwner;
        await _context.SaveChangesAsync();

        if (s.Enabled && !(branch.HoursEnabled && branch.OpeningHours?.Any() == true))
            TempData["Warning"] = "Saved, but guests can't book yet: bookable times come from your opening hours. Add them in Edit branch → Opening hours.";
        else
            TempData["Success"] = s.Enabled ? "Booking settings saved. Guests can book online." : "Booking settings saved. Online booking is off.";
        return RedirectToAction(nameof(Settings), new { id });
    }

    /// <summary>The booking page's QR code (PNG), for flyers, the window or an Instagram story.</summary>
    [HttpGet("qr.png")]
    public async Task<IActionResult> Qr(int id)
    {
        var branch = await BranchAsync(id, BranchPermission.EditBranch);
        if (branch == null) return NotFound();
        return File(_qr.Png(BookingUrl(branch)), "image/png", $"{SeoService.Slug(branch.Name)}-booking-qr.png");
    }
}

public record BookingDay(Branch Branch, ReservationSettings Settings, DateOnly Date, DateOnly Today, IReadOnlyList<Reservation> Reservations,
    IReadOnlyList<BookingSlot> Slots, bool DayClosed, bool Bookable, string Stamp);

public record BookingSettingsPage(Branch Branch, ReservationSettings Settings, IReadOnlyList<string>? Errors);
