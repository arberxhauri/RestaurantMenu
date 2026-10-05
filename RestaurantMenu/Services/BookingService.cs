using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>What the booking page sends.</summary>
public class BookRequest
{
    public Guid RequestId { get; set; }
    public string? Date { get; set; }   // yyyy-MM-dd, branch local
    public string? Time { get; set; }   // HH:mm
    public int Guests { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Note { get; set; }
    public string? Lang { get; set; }
}

public enum BookProblem { Unavailable, SlotTaken, Name, Phone, Email, PartySize, TooMany, Invalid }

public record BookResult(Reservation? Reservation, BookProblem? Problem, string? Message);

/// <summary>
/// Table bookings: availability, booking (seats checked and taken under a per-branch lock,
/// so two guests can never both get the last seats), the guest's cancel, the staff's
/// status changes, and the messages that go with them (SMS when set up, email when the
/// guest gave one, and an email to the owner about new online bookings).
/// </summary>
public class BookingService
{
    private readonly ApplicationDbContext _db;
    private readonly SeoService _seo;
    private readonly SmsService _sms;
    private readonly EmailService _email;
    private readonly EmailQueue _emailQueue;
    private readonly BackgroundJobs _jobs;

    public BookingService(ApplicationDbContext db, SeoService seo, SmsService sms, EmailService email, EmailQueue emailQueue, BackgroundJobs jobs)
    {
        _db = db;
        _seo = seo;
        _sms = sms;
        _email = email;
        _emailQueue = emailQueue;
        _jobs = jobs;
    }

    /// <summary>The saved settings, or the defaults (off) for a branch that never opened them.</summary>
    public async Task<ReservationSettings> SettingsAsync(int branchId) =>
        await _db.ReservationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branchId)
        ?? new ReservationSettings { BranchId = branchId };

    /// <summary>Guests holding seats per slot start (local time) on a day and the early hours after it.</summary>
    public async Task<Dictionary<DateTime, int>> BookedAsync(int branchId, DateOnly date)
    {
        var from = date.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(2);
        return await _db.Reservations.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.StartsAtLocal >= from && r.StartsAtLocal < to
                        && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated))
            .GroupBy(r => r.StartsAtLocal)
            .Select(g => new { g.Key, Guests = g.Sum(r => r.Guests) })
            .ToDictionaryAsync(x => x.Key, x => x.Guests);
    }

    public async Task<List<BookingSlot>> SlotsAsync(Branch branch, ReservationSettings settings, DateOnly date, DateTime utcNow) =>
        BookingRules.Slots(settings, branch.OpeningHours ?? new List<BranchHours>(), OpeningHours.Zone(branch.TimeZone), date, utcNow,
            await BookedAsync(branch.Id, date));

    /// <summary>Whether guests can book online at all: switched on, and opening hours entered.</summary>
    public static bool IsBookable(Branch branch, ReservationSettings s) => s.Enabled && branch.HoursEnabled && branch.OpeningHours?.Any() == true;

    public string GuestLink(Branch branch, Reservation r) => _seo.Url($"/book/{branch.Slug}/r/{r.PublicId}");

    // ---------------------------------------------------------------- guest booking

    public async Task<BookResult> BookAsync(Branch branch, BookRequest req, DateTime utcNow)
    {
        var settings = await SettingsAsync(branch.Id);
        var lang = SupportedLanguage(branch, req.Lang);
        var w = BookingText.For(lang);
        BookResult Fail(BookProblem p, string message) => new(null, p, message);

        if (!IsBookable(branch, settings)) return Fail(BookProblem.Unavailable, string.Format(w.Unavailable, branch.PhoneNumber));
        if (req.RequestId == Guid.Empty
            || !DateOnly.TryParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !TimeOnly.TryParseExact(req.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return Fail(BookProblem.Invalid, w.SlotTaken);
        if (req.Guests < 1 || req.Guests > settings.MaxPartySize)
            return Fail(BookProblem.PartySize, string.Format(w.LargeGroup, settings.MaxPartySize, branch.PhoneNumber));

        var name = BookingRules.Clean(req.Name, BookingRules.MaxNameLength);
        if (name == null) return Fail(BookProblem.Name, w.NameRequired);
        var phone = BookingRules.NormalizePhone(req.Phone, settings.CountryCode);
        if (phone == null) return Fail(BookProblem.Phone, w.PhoneInvalid);
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        if (email != null && !BookingRules.IsEmail(email)) return Fail(BookProblem.Email, w.EmailInvalid);

        // A slot that starts after midnight belongs to the evening before; find it on either day.
        var local = date.ToDateTime(time);
        var serviceDay = date;
        var slot = (await SlotsAsync(branch, settings, date, utcNow)).FirstOrDefault(s => s.Local == local);
        if (slot == null)
        {
            serviceDay = date.AddDays(-1);
            slot = (await SlotsAsync(branch, settings, serviceDay, utcNow)).FirstOrDefault(s => s.Local == local);
        }
        if (slot == null || !slot.Open) return Fail(BookProblem.SlotTaken, w.SlotTaken);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            // One booking at a time per branch from here to commit: the seat count below
            // can't change under us.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Branches\" WHERE \"Id\" = {branch.Id} FOR UPDATE");

            var existing = await _db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.BranchId == branch.Id && r.ClientRequestId == req.RequestId);
            if (existing != null) return new BookResult(existing, null, null); // the same attempt again

            var held = await _db.Reservations
                .Where(r => r.BranchId == branch.Id && r.StartsAtLocal == local
                            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated))
                .SumAsync(r => (int?)r.Guests) ?? 0;
            if (held + req.Guests > settings.CoversPerSlot) return Fail(BookProblem.SlotTaken, w.SlotTaken);

            var active = await _db.Reservations.CountAsync(r => r.BranchId == branch.Id && r.Phone == phone && r.StartsAtUtc > utcNow
                && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed));
            if (active >= BookingRules.MaxActivePerPhone) return Fail(BookProblem.TooMany, w.TooMany);

            var reservation = new Reservation
            {
                PublicId = Guid.NewGuid(),
                ClientRequestId = req.RequestId,
                BranchId = branch.Id,
                Name = name,
                Phone = phone,
                Email = email,
                Guests = req.Guests,
                StartsAtUtc = slot.Utc,
                StartsAtLocal = local,
                Status = settings.AutoConfirm ? ReservationStatus.Confirmed : ReservationStatus.Pending,
                Note = BookingRules.Clean(req.Note, BookingRules.MaxNoteLength),
                Language = lang,
                Source = "online",
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow
            };
            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            NotifyGuest(branch, reservation, reservation.Status == ReservationStatus.Confirmed ? MessageKind.Confirmed : MessageKind.Pending);
            if (settings.NotifyOwner) await NotifyOwnerAsync(branch, reservation, isNew: true);
            return new BookResult(reservation, null, null);
        });
    }

    private static string SupportedLanguage(Branch branch, string? lang)
    {
        var languages = branch.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lang != null && languages.Contains(lang) ? lang : languages.FirstOrDefault() ?? "en";
    }

    /// <summary>The guest cancels from their link: only a booking that still holds seats and hasn't started.</summary>
    public async Task<bool> CancelByGuestAsync(Branch branch, Reservation r, DateTime utcNow)
    {
        if (r.StartsAtUtc <= utcNow) return false;
        var changed = await _db.Reservations
            .Where(x => x.Id == r.Id && (x.Status == ReservationStatus.Pending || x.Status == ReservationStatus.Confirmed))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReservationStatus.Cancelled)
                .SetProperty(x => x.CancelledBy, "guest").SetProperty(x => x.UpdatedUtc, utcNow)) == 1;
        if (changed)
        {
            r.Status = ReservationStatus.Cancelled;
            var settings = await SettingsAsync(branch.Id);
            if (settings.NotifyOwner) await NotifyOwnerAsync(branch, r, isNew: false);
        }
        return changed;
    }

    // ---------------------------------------------------------------- staff

    /// <summary>
    /// Moves a booking from <paramref name="from"/> to <paramref name="to"/> atomically (a
    /// second screen with an older view gets Conflict). Confirming a pending booking and
    /// cancelling one tell the guest.
    /// </summary>
    public async Task<(Reservation? Reservation, bool Conflict)> SetStatusAsync(Branch branch, int id, ReservationStatus from, ReservationStatus to, DateTime utcNow)
    {
        var changed = from != to && await _db.Reservations
            .Where(r => r.Id == id && r.BranchId == branch.Id && r.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to)
                .SetProperty(r => r.CancelledBy, r => to == ReservationStatus.Cancelled ? "restaurant" : null)
                .SetProperty(r => r.UpdatedUtc, utcNow)) == 1;
        var r = await _db.Reservations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branch.Id);
        if (r == null) return (null, false);
        if (changed && r.StartsAtUtc > utcNow)
        {
            if (from == ReservationStatus.Pending && to == ReservationStatus.Confirmed) NotifyGuest(branch, r, MessageKind.Confirmed);
            if (to == ReservationStatus.Cancelled && r.Source == "online") NotifyGuest(branch, r, MessageKind.Cancelled);
        }
        return (r, !changed);
    }

    /// <summary>A booking taken by phone or for a walk-in. Staff may overbook; capacity is shown, not enforced.</summary>
    public async Task<Reservation> AddByStaffAsync(Branch branch, DateTime local, int guests, string name, string? phone, string? note, DateTime utcNow)
    {
        var zone = OpeningHours.Zone(branch.TimeZone);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A time in the spring-forward gap is moved to the first valid minute after it.
        while (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(15);
        var r = new Reservation
        {
            PublicId = Guid.NewGuid(),
            BranchId = branch.Id,
            Name = name,
            Phone = phone ?? "",
            Guests = guests,
            StartsAtLocal = unspecified,
            StartsAtUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone),
            Status = ReservationStatus.Confirmed,
            StaffNote = note,
            Language = SupportedLanguage(branch, null),
            Source = "staff",
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow
        };
        _db.Reservations.Add(r);
        await _db.SaveChangesAsync();
        return r;
    }

    /// <summary>No-shows and visits of one phone number at a branch, for the "2 no-shows before" hint.</summary>
    public async Task<Dictionary<string, (int NoShows, int Visits)>> HistoryAsync(int branchId, IEnumerable<string> phones, DateTime beforeUtc)
    {
        var list = phones.Where(p => p.Length > 0).Distinct().ToList();
        if (list.Count == 0) return new();
        var rows = await _db.Reservations.AsNoTracking()
            .Where(r => r.BranchId == branchId && list.Contains(r.Phone) && r.StartsAtUtc < beforeUtc
                        && (r.Status == ReservationStatus.NoShow || r.Status == ReservationStatus.Seated))
            .GroupBy(r => r.Phone)
            .Select(g => new { g.Key, NoShows = g.Count(r => r.Status == ReservationStatus.NoShow), Visits = g.Count(r => r.Status == ReservationStatus.Seated) })
            .ToListAsync();
        return rows.ToDictionary(x => x.Key, x => (x.NoShows, x.Visits));
    }

    // ---------------------------------------------------------------- messages

    public enum MessageKind { Confirmed, Pending, Cancelled, Reminder }

    /// <summary>SMS (queued) when set up and the guest gave a phone; email (queued) when they gave an email.</summary>
    public void NotifyGuest(Branch branch, Reservation r, MessageKind kind, string? link = null)
    {
        link ??= GuestLink(branch, r);
        var w = BookingText.For(r.Language);
        var template = kind switch
        {
            MessageKind.Pending => w.MsgPending,
            MessageKind.Cancelled => w.MsgCancelled,
            MessageKind.Reminder => w.MsgReminder,
            _ => w.MsgConfirmed
        };
        var text = BookingText.Message(template, branch.Name, r, link);

        if (_sms.IsConfigured && r.Phone.Length > 0)
        {
            var to = r.Phone;
            _jobs.Enqueue("booking sms", (sp, ct) => sp.GetRequiredService<SmsService>().SendAsync(to, text, ct));
        }
        if (_email.IsConfigured && r.Email != null)
        {
            var title = kind switch
            {
                MessageKind.Pending => w.PendingTitle,
                MessageKind.Cancelled => w.CancelledTitle,
                _ => w.ConfirmedTitle
            };
            var (html, plain) = EmailTemplate.Render(r.Name, text, new[] { text }, title, link);
            _emailQueue.Enqueue(new EmailMessage(r.Email, r.Name, string.Format(w.EmailSubject, branch.Name), html, plain));
        }
    }

    private async Task NotifyOwnerAsync(Branch branch, Reservation r, bool isNew)
    {
        if (!_email.IsConfigured) return;
        var owner = await _db.Users.AsNoTracking().Where(u => u.Id == branch.UserId).Select(u => new { u.Email, u.FullName }).FirstOrDefaultAsync();
        if (owner?.Email == null) return;
        var when = r.StartsAtLocal.ToString("ddd d MMM, HH:mm", CultureInfo.GetCultureInfo("en-GB"));
        var what = $"{r.Name}, {r.Guests} {(r.Guests == 1 ? "guest" : "guests")}, {when}";
        var link = _seo.Url($"/branch/{branch.Id}/bookings?date={r.StartsAtLocal:yyyy-MM-dd}");
        var lines = new List<string>
        {
            isNew ? $"New booking at {branch.Name}: {what}." : $"{r.Name} cancelled their booking at {branch.Name} ({when}).",
        };
        if (isNew)
        {
            lines.Add($"Phone: {BookingRules.FormatPhone(r.Phone)}" + (r.Note != null ? $". Request: {r.Note}" : ""));
            if (r.Status == ReservationStatus.Pending) lines.Add("It's waiting for your confirmation.");
        }
        var (html, text) = EmailTemplate.Render(owner.FullName, lines[0], lines, "Open bookings", link);
        _emailQueue.Enqueue(new EmailMessage(owner.Email, owner.FullName,
            isNew ? $"New booking: {what}" : $"Cancelled: {r.Name}, {when}", html, text));
    }
}
