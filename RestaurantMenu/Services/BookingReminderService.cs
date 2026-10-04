using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Every 5 minutes: reminds guests of confirmed bookings starting within their branch's
/// ReminderHours (by SMS and/or email, whatever the booking has). Each booking is claimed
/// with one atomic update before sending, so a reminder never goes out twice. Bookings made
/// inside the reminder window are skipped: their confirmation was just sent.
/// Links need a public address outside a request: Seo__BaseUrl, else Render's RENDER_EXTERNAL_URL.
/// </summary>
public class BookingReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingReminderService> _logger;
    private readonly string? _baseUrl;
    private readonly TimeSpan _interval;

    public BookingReminderService(IServiceScopeFactory scopes, ILogger<BookingReminderService> logger, IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        _baseUrl = (config["Seo:BaseUrl"] ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL"))?.TrimEnd('/');
        _interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Bookings:ReminderIntervalSeconds", 300), 10, 3600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Booking reminders failed; retrying later");
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public async Task<int> RunOnceAsync(DateTime utcNow, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var due = await db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Confirmed && r.ReminderSentUtc == null && r.StartsAtUtc > utcNow && r.Source == "online")
            .Join(db.ReservationSettings.Where(s => s.Enabled && s.ReminderHours > 0), r => r.BranchId, s => s.BranchId, (r, s) => new { r, s.ReminderHours })
            .Where(x => x.r.StartsAtUtc <= utcNow.AddHours(x.ReminderHours) && x.r.CreatedUtc <= x.r.StartsAtUtc.AddHours(-x.ReminderHours))
            .Select(x => x.r)
            .Take(200)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var r in due)
        {
            var claimed = await db.Reservations.Where(x => x.Id == r.Id && x.ReminderSentUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderSentUtc, utcNow), ct) == 1;
            if (!claimed) continue;
            var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == r.BranchId, ct);
            if (branch == null) continue;
            var link = _baseUrl != null ? $"{_baseUrl}/book/{SeoService.Slug(branch.Name)}/r/{r.PublicId}" : branch.PhoneNumber;
            bookings.NotifyGuest(branch, r, BookingService.MessageKind.Reminder, link);
            sent++;
        }
        if (sent > 0) _logger.LogInformation("Booking reminders: {Count} sent", sent);
        return sent;
    }
}
