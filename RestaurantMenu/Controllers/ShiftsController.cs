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
/// The weekly rota of a branch: who works when (Editor and up see it; Manager and up plan it).
/// People are team members or anyone typed by name. One person can't be in two places at once.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/shifts")]
public class ShiftsController : Controller
{
    public const int MaxShiftHours = 16;
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB"); // back office format, whatever the server culture

    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;

    public ShiftsController(ApplicationDbContext context, IBranchAccess access)
    {
        _context = context;
        _access = access;
    }

    private Task<Branch?> BranchAsync(int id, BranchPermission p)
    {
        var allowed = _access.BranchIds(p);
        return _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    public static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private IActionResult Week(int id, DateOnly anyDay) =>
        RedirectToAction(nameof(Index), new { id, week = Monday(anyDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });

    [HttpGet("")]
    public async Task<IActionResult> Index(int id, string? week)
    {
        var branch = await BranchAsync(id, BranchPermission.Stock);
        if (branch == null) return NotFound();
        var today = BookingRules.Today(OpeningHours.Zone(branch.TimeZone), DateTime.UtcNow);
        var monday = Monday(ParseDate(week) ?? today);
        var from = monday.ToDateTime(TimeOnly.MinValue);
        var shifts = await _context.Shifts.AsNoTracking().Where(s => s.BranchId == id && s.StartsLocal >= from && s.StartsLocal < from.AddDays(7))
            .OrderBy(s => s.StartsLocal).ToListAsync();
        ViewBag.CanPlan = await _access.CanAsync(id, BranchPermission.EditBranch);
        ViewBag.Team = await TeamAsync(branch);
        ViewBag.PrevWeekCount = await _context.Shifts.CountAsync(s => s.BranchId == id && s.StartsLocal >= from.AddDays(-7) && s.StartsLocal < from);
        return View(new ShiftsWeek(branch, monday, today, shifts));
    }

    /// <summary>The owner and team members, for the person picker: (user id, name).</summary>
    private async Task<List<(string Id, string Name)>> TeamAsync(Branch branch)
    {
        var owner = await _context.Users.AsNoTracking().Where(u => u.Id == branch.UserId).Select(u => new { u.Id, u.FullName }).FirstOrDefaultAsync();
        var members = await _context.BranchMembers.AsNoTracking().Where(m => m.BranchId == branch.Id)
            .Select(m => new { m.UserId, m.User!.FullName }).OrderBy(m => m.FullName).ToListAsync();
        var list = new List<(string, string)>();
        if (owner != null) list.Add((owner.Id, owner.FullName));
        list.AddRange(members.Select(m => (m.UserId, m.FullName)));
        return list;
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, int? shiftId, string? person, string? name, string? date, string? start, string? end, string? station, string? note)
    {
        var branch = await BranchAsync(id, BranchPermission.EditBranch);
        if (branch == null) return NotFound();
        var day = ParseDate(date);
        var formats = new[] { "HH:mm", "H:mm" };
        if (day == null || !TimeOnly.TryParseExact(start, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)
            || !TimeOnly.TryParseExact(end, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
        {
            TempData["Error"] = "Choose a day and times like 09:00 and 17:00.";
            return day == null ? RedirectToAction(nameof(Index), new { id }) : Week(id, day.Value);
        }
        var startsAt = day.Value.ToDateTime(s);
        var endsAt = day.Value.ToDateTime(e);
        if (endsAt <= startsAt) endsAt = endsAt.AddDays(1); // ends after midnight
        if ((endsAt - startsAt).TotalHours > MaxShiftHours)
        {
            TempData["Error"] = $"A shift can be at most {MaxShiftHours} hours.";
            return Week(id, day.Value);
        }

        // A team member, or a name typed for someone without an account.
        var team = await TeamAsync(branch);
        string? userId = null;
        string? personName;
        if (!string.IsNullOrEmpty(person) && person != "other")
        {
            var match = team.FirstOrDefault(t => t.Id == person);
            if (match.Id == null) return BadRequest();
            (userId, personName) = (match.Id, match.Name);
        }
        else
        {
            personName = Clean(name, 80);
        }
        if (personName == null)
        {
            TempData["Error"] = "Choose who works this shift, or type their name.";
            return Week(id, day.Value);
        }

        var overlap = await _context.Shifts.AsNoTracking().Where(x => x.BranchId == id && x.Id != (shiftId ?? 0)
                && (userId != null ? x.UserId == userId : x.UserId == null && x.PersonName.ToLower() == personName.ToLower())
                && x.StartsLocal < endsAt && startsAt < x.EndsLocal).FirstOrDefaultAsync();
        if (overlap != null)
        {
            TempData["Error"] = $"{personName} already works {overlap.StartsLocal.ToString("ddd HH:mm", En)}–{overlap.EndsLocal.ToString("HH:mm", En)} then.";
            return Week(id, day.Value);
        }

        Shift shift;
        if (shiftId is { } sid)
        {
            var found = await _context.Shifts.FirstOrDefaultAsync(x => x.Id == sid && x.BranchId == id);
            if (found == null) return NotFound();
            shift = found;
        }
        else
        {
            shift = new Shift { BranchId = id, CreatedUtc = DateTime.UtcNow };
            _context.Shifts.Add(shift);
        }
        shift.UserId = userId;
        shift.PersonName = personName;
        shift.StartsLocal = startsAt;
        shift.EndsLocal = endsAt;
        shift.Station = Clean(station, 40);
        shift.Note = Clean(note, 200);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{personName}: {startsAt.ToString("ddd d MMM HH:mm", En)}–{endsAt.ToString("HH:mm", En)} saved.";
        return Week(id, day.Value);
    }

    [HttpPost("{shiftId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int shiftId)
    {
        if (await BranchAsync(id, BranchPermission.EditBranch) == null) return NotFound();
        var shift = await _context.Shifts.FirstOrDefaultAsync(x => x.Id == shiftId && x.BranchId == id);
        if (shift == null) return NotFound();
        _context.Shifts.Remove(shift);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{shift.PersonName}'s shift on {shift.StartsLocal.ToString("ddd d MMM", En)} removed.";
        return Week(id, DateOnly.FromDateTime(shift.StartsLocal));
    }

    /// <summary>Copies last week's shifts into this week, skipping any that would clash.</summary>
    [HttpPost("copy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyLastWeek(int id, string? week)
    {
        if (await BranchAsync(id, BranchPermission.EditBranch) == null) return NotFound();
        var monday = ParseDate(week) is { } w ? Monday(w) : (DateOnly?)null;
        if (monday == null) return BadRequest();
        var from = monday.Value.ToDateTime(TimeOnly.MinValue);
        var last = await _context.Shifts.AsNoTracking().Where(s => s.BranchId == id && s.StartsLocal >= from.AddDays(-7) && s.StartsLocal < from).ToListAsync();
        var existing = await _context.Shifts.AsNoTracking().Where(s => s.BranchId == id && s.StartsLocal >= from.AddDays(-1) && s.StartsLocal < from.AddDays(8)).ToListAsync();
        int copied = 0, skipped = 0;
        foreach (var s in last)
        {
            var (a, b) = (s.StartsLocal.AddDays(7), s.EndsLocal.AddDays(7));
            var clash = existing.Any(x => (s.UserId != null ? x.UserId == s.UserId : x.UserId == null && x.PersonName.ToLower() == s.PersonName.ToLower())
                                          && x.StartsLocal < b && a < x.EndsLocal);
            if (clash) { skipped++; continue; }
            var copy = new Shift { BranchId = id, UserId = s.UserId, PersonName = s.PersonName, Station = s.Station, Note = s.Note, StartsLocal = a, EndsLocal = b, CreatedUtc = DateTime.UtcNow };
            _context.Shifts.Add(copy);
            existing.Add(copy);
            copied++;
        }
        await _context.SaveChangesAsync();
        TempData[copied > 0 ? "Success" : "Warning"] = copied == 0 && skipped == 0 ? "Last week has no shifts to copy."
            : $"{copied} shift{(copied == 1 ? "" : "s")} copied from last week" + (skipped > 0 ? $"; {skipped} skipped because they'd clash." : ".");
        return Week(id, monday.Value);
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = new string(s.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }
}

public record ShiftsWeek(Branch Branch, DateOnly Monday, DateOnly Today, IReadOnlyList<Shift> Shifts);
