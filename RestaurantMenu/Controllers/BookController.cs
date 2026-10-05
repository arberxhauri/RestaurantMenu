using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// The public booking page (/book/{slug}), linked from the menu and shareable on Instagram.
/// Works without JavaScript (a plain form; dates are links, times are radio buttons); with
/// it, book.js updates the times in place and books without a reload. The guest's link
/// (/book/{slug}/r/{id}) shows the booking, adds it to a calendar and cancels it.
/// </summary>
public class BookController : Controller
{
    /// <summary>Date chips shown; later dates through the date field.</summary>
    public const int DateChips = 14;

    private readonly ApplicationDbContext _context;
    private readonly BookingService _bookings;
    private readonly SeoService _seo;
    private readonly BranchSlugs _slugs;

    public BookController(ApplicationDbContext context, BookingService bookings, SeoService seo, BranchSlugs slugs)
    {
        _slugs = slugs;
        _context = context;
        _bookings = bookings;
        _seo = seo;
    }

    /// <summary>The branch by its link or an old one; Index redirects old links to the current one.</summary>
    private async Task<Branch?> BranchAsync(string slug)
    {
        var match = await _slugs.ResolveAsync(slug);
        if (match == null) return null;
        return await _context.Branches.AsNoTracking().Include(b => b.OpeningHours)
            .FirstOrDefaultAsync(b => b.Id == match.BranchId);
    }

    private static string Language(Branch b, string? lang)
    {
        var languages = b.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lang != null && languages.Contains(lang) ? lang : languages.FirstOrDefault() ?? "en";
    }

    [HttpGet("book/{slug}")]
    public async Task<IActionResult> Index(string slug, string? lang, string? date, int guests = 2)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var canonical = branch.Slug;
        if (!string.Equals(slug, canonical, StringComparison.Ordinal))
        {
            return RedirectPermanent($"/book/{canonical}{Request.QueryString}");
        }
        return View(await PageAsync(branch, Language(branch, lang), date, guests, new BookRequest(), null));
    }

    private async Task<BookPage> PageAsync(Branch branch, string lang, string? date, int guests, BookRequest form, Dictionary<string, string>? errors)
    {
        var settings = await _bookings.SettingsAsync(branch.Id);
        var zone = OpeningHours.Zone(branch.TimeZone);
        var now = DateTime.UtcNow;
        var today = BookingRules.Today(zone, now);
        var last = today.AddDays(Math.Max(0, settings.MaxDaysAhead));
        var closed = BookingRules.ClosedDates(settings.ClosedDates);
        var openDays = (branch.OpeningHours ?? new List<BranchHours>()).Select(h => h.DayOfWeek).ToHashSet();
        bool DayOpen(DateOnly d) => openDays.Contains(d.DayOfWeek) && !closed.Contains(d);

        guests = Math.Clamp(guests, 1, Math.Max(1, settings.MaxPartySize));
        var selected = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d0) && d0 >= today && d0 <= last ? d0 : today;

        var bookable = BookingService.IsBookable(branch, settings);
        var slots = bookable ? await _bookings.SlotsAsync(branch, settings, selected, now) : new List<BookingSlot>();
        // Nothing left today (late evening): start on the next day that has a free time.
        if (bookable && date == null && !slots.Any(s => s.Open && s.Remaining >= guests))
        {
            for (var d = today.AddDays(1); d <= last && d <= today.AddDays(DateChips); d = d.AddDays(1))
            {
                if (!DayOpen(d)) continue;
                var next = await _bookings.SlotsAsync(branch, settings, d, now);
                if (next.Any(s => s.Open && s.Remaining >= guests)) { selected = d; slots = next; break; }
            }
        }

        var dates = Enumerable.Range(0, DateChips).Select(i => today.AddDays(i)).Where(d => d <= last)
            .Select(d => new BookDate(d, DayOpen(d))).ToList();

        return new BookPage(branch, settings, lang, branch.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            bookable, today, last, dates, selected, guests, slots, form, errors ?? new Dictionary<string, string>(),
            selected == today ? BookingText.For(lang).Today : selected == today.AddDays(1) ? BookingText.For(lang).Tomorrow : null);
    }

    /// <summary>Times for a day and party size (book.js): { date, closed, slots: [{ time, open }] }.</summary>
    [HttpGet("book/{slug}/slots")]
    public async Task<IActionResult> Slots(string slug, string? date, int guests = 2)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var settings = await _bookings.SettingsAsync(branch.Id);
        if (!BookingService.IsBookable(branch, settings)) return Ok(new { date, closed = true, slots = Array.Empty<object>() });
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return BadRequest();
        guests = Math.Clamp(guests, 1, Math.Max(1, settings.MaxPartySize));
        var slots = await _bookings.SlotsAsync(branch, settings, day, DateTime.UtcNow);
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            date,
            closed = BookingRules.ClosedDates(settings.ClosedDates).Contains(day) || slots.Count == 0,
            slots = slots.Select(s => new { time = s.Time, date = s.Local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), open = s.Open && s.Remaining >= guests })
        });
    }

    /// <summary>
    /// Books a table. A plain form post answers with a redirect to the booking (or the page
    /// again with the problems); book.js asks for JSON and gets { ok, url } or { problem, field, message }.
    /// </summary>
    [HttpPost("book/{slug}")]
    [EnableRateLimiting("bookings")]
    [MaxBodySize(16 * 1024)]
    public async Task<IActionResult> Reserve(string slug, [FromForm] BookRequest form)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return NotFound();
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json");
        // Without JavaScript there is no request id: one per post (a resubmitted form may book twice, as on any site).
        if (form.RequestId == Guid.Empty && !wantsJson) form.RequestId = Guid.NewGuid();
        // A time radio carries "yyyy-MM-ddTHH:mm" so slots after midnight keep their real date.
        if (form.Time?.Length == 16 && form.Time[10] == 'T') { form.Date = form.Time[..10]; form.Time = form.Time[11..]; }

        var result = await _bookings.BookAsync(branch, form, DateTime.UtcNow);
        if (result.Reservation != null)
        {
            var url = $"/book/{branch.Slug}/r/{result.Reservation.PublicId}";
            if (!wantsJson) TempData["JustBooked"] = true;
            return wantsJson ? Ok(new { ok = true, url }) : Redirect(url);
        }

        var field = result.Problem switch
        {
            BookProblem.Name => "name",
            BookProblem.Phone or BookProblem.TooMany => "phone",
            BookProblem.Email => "email",
            BookProblem.PartySize => "guests",
            _ => "time"
        };
        if (wantsJson)
        {
            return UnprocessableEntity(new { ok = false, problem = result.Problem.ToString()!.ToLowerInvariant(), field, message = result.Message });
        }
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return View("Index", await PageAsync(branch, Language(branch, form.Lang), form.Date, form.Guests, form,
            new Dictionary<string, string> { [field] = result.Message! }));
    }

    private async Task<(Branch? Branch, Reservation? Reservation)> ReservationAsync(string slug, Guid id)
    {
        var branch = await BranchAsync(slug);
        if (branch == null) return (null, null);
        var r = await _context.Reservations.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == id && x.BranchId == branch.Id);
        return (branch, r);
    }

    [HttpGet("book/{slug}/r/{id:guid}")]
    public async Task<IActionResult> Reservation(string slug, Guid id)
    {
        var (branch, r) = await ReservationAsync(slug, id);
        if (branch == null || r == null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        ViewBag.JustBooked = TempData["JustBooked"] is true;
        return View(new ReservationPage(branch, r, r.Language, r.StartsAtUtc <= DateTime.UtcNow, _seo.MenuUrl(branch.Slug, r.Language == "en" ? null : r.Language)));
    }

    [HttpGet("book/{slug}/r/{id:guid}/calendar.ics")]
    public async Task<IActionResult> Calendar(string slug, Guid id)
    {
        var (branch, r) = await ReservationAsync(slug, id);
        if (branch == null || r == null) return NotFound();
        var ics = BookingRules.Ics(r, branch.Name, branch.Address, _bookings.GuestLink(branch, r));
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", $"{branch.Slug}-booking.ics");
    }

    [HttpPost("book/{slug}/r/{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("bookings")]
    public async Task<IActionResult> Cancel(string slug, Guid id)
    {
        var (branch, r) = await ReservationAsync(slug, id);
        if (branch == null || r == null) return NotFound();
        await _bookings.CancelByGuestAsync(branch, r, DateTime.UtcNow);
        return Redirect($"/book/{branch.Slug}/r/{id}");
    }
}

public record BookDate(DateOnly Date, bool Open);

public record BookPage(Branch Branch, ReservationSettings Settings, string Language, string[] Languages, bool Bookable,
    DateOnly Today, DateOnly LastDate, IReadOnlyList<BookDate> Dates, DateOnly Selected, int Guests,
    IReadOnlyList<BookingSlot> Slots, BookRequest Form, IReadOnlyDictionary<string, string> Errors, string? SelectedLabel);

public record ReservationPage(Branch Branch, Reservation Reservation, string Language, bool IsPast, string MenuUrl);
