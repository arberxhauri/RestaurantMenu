using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>What one billing run did, for the log and the admin's "Run billing checks now".</summary>
public record BillingRunResult(int Events, int StatusChanges, int Invoices, int Emails, List<string> Problems)
{
    public override string ToString() =>
        $"{Events} payment event(s), {StatusChanges} status change(s), {Invoices} invoice(s) issued, {Emails} email(s)" +
        (Problems.Count == 0 ? "" : $"; {Problems.Count} problem(s): {string.Join(" ", Problems)}");
}

/// <summary>
/// The billing work that doesn't wait for a click, all in the database so restarts lose nothing
/// and several instances can't do it twice:
/// - the inbox: each unprocessed BillingEvent is applied in its own transaction, the row locked
///   FOR UPDATE SKIP LOCKED; failures are retried with backoff and reported after 10 attempts;
/// - the lifecycle (one run at a time, advisory lock): stored status follows the clock
///   (SubscriptionRules.Advance), renewal invoices are issued before a paid period ends, and
///   the owner gets the invoice, reminder, past-due, read-only and trial emails (BillingMailer).
/// </summary>
public class BillingRunner
{
    private const long LifecycleLockKey = 0x4D514D_4C494645; // "MQM LIFE"

    private readonly ApplicationDbContext _db;
    private readonly BillingService _billing;
    private readonly BillingMailer _mailer;
    private readonly PlanSettingsService _settings;
    private readonly ILogger<BillingRunner> _logger;

    public BillingRunner(ApplicationDbContext db, BillingService billing, BillingMailer mailer, PlanSettingsService settings, ILogger<BillingRunner> logger)
    {
        _db = db;
        _billing = billing;
        _mailer = mailer;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------- inbox

    /// <summary>Applies waiting inbox events, oldest first. Returns how many were applied.</summary>
    public async Task<int> ProcessEventsAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var applied = 0;
        for (var n = 0; n < 50; n++)
        {
            var paid = (Invoice?)null;
            var strategy = _db.Database.CreateExecutionStrategy();
            var outcome = await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var e = (await _db.BillingEvents.FromSqlInterpolated($"""
                    SELECT * FROM "BillingEvents"
                    WHERE "ProcessedUtc" IS NULL AND ("NextAttemptUtc" IS NULL OR "NextAttemptUtc" <= {utcNow})
                    ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                    """).ToListAsync(ct)).FirstOrDefault(); // not composed: the lock must stay on the outer query
                if (e == null) return (int?)null;
                try
                {
                    paid = await _billing.ApplyAsync(e, utcNow);
                    e.ProcessedUtc = utcNow;
                    e.LastError = null;
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return e.Id;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await tx.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    var row = await _db.BillingEvents.FirstAsync(x => x.Id == e.Id, ct);
                    row.Attempts++;
                    row.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    row.NextAttemptUtc = utcNow.AddMinutes(Math.Min(60, Math.Pow(2, row.Attempts)));
                    await _db.SaveChangesAsync(ct);
                    if (row.Attempts == 10)
                        _logger.LogError("Billing: event {Provider}/{EventId} failed 10 times: {Error}", row.Provider, row.EventId, row.LastError);
                    else
                        _logger.LogWarning(ex, "Billing: event {Provider}/{EventId} failed (attempt {Attempts}), retrying", row.Provider, row.EventId, row.Attempts);
                    return -1;
                }
            });
            if (outcome == null) break;
            if (outcome > 0)
            {
                applied++;
                if (paid != null) await _billing.ThankAsync(paid);
            }
        }
        return applied;
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>One lifecycle pass. Skipped (empty result) when another instance is running one.</summary>
    public async Task<BillingRunResult> RunAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var problems = new List<string>();
        var events = await ProcessEventsAsync(utcNow, ct);

        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            var got = (await _db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LifecycleLockKey}) AS \"Value\"").ToListAsync(ct)).Single();
            if (!got) return new BillingRunResult(events, 0, 0, 0, problems);
            try
            {
                var (changes, invoices, emails) = await LifecycleAsync(utcNow, problems, ct);
                return new BillingRunResult(events, changes, invoices, emails, problems);
            }
            finally
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({LifecycleLockKey})", ct);
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task<(int Changes, int Invoices, int Emails)> LifecycleAsync(DateTime utcNow, List<string> problems, CancellationToken ct)
    {
        var settings = await _settings.GetAsync();
        int changes = 0, issued = 0, emails = 0;

        // Removed owners are left out by the query filter; a signup's unstarted trial never moves.
        var subs = await _db.Subscriptions.Include(s => s.Items).Include(s => s.Owner).Where(s => !s.IsLegacy).ToListAsync(ct);
        foreach (var sub in subs)
        {
            var owner = sub.Owner!;
            var w = BillingText.For(owner.Language);
            var lang = owner.Language;

            // 1. The stored status follows the clock.
            var before = BillingService.State(sub);
            var after = SubscriptionRules.Advance(before, utcNow, settings.GraceDays);
            if (after != before)
            {
                sub.Status = after.Status;
                sub.GraceEndsUtc = after.GraceEndsUtc;
                sub.UpdatedUtc = utcNow;
                if (after.Status != before.Status)
                {
                    _db.SubscriptionAudits.Add(new SubscriptionAudit { SubscriptionId = sub.Id, Action = $"{before.Status} → {after.Status} (time)", AtUtc = utcNow });
                    changes++;
                }
                await _db.SaveChangesAsync(ct);

                var open = await _db.Invoices.AsNoTracking().Where(i => i.SubscriptionId == sub.Id && i.Status == InvoiceStatus.Open)
                    .OrderBy(i => i.PeriodStartUtc).FirstOrDefaultAsync(ct);
                var number = open?.Number ?? "-";
                var sent = SubscriptionRules.TransitionEmail(before.Status, after.Status) switch
                {
                    "past-due" => await _mailer.SendOnceAsync(owner, "past-due", SubscriptionRules.Key(sub.CurrentPeriodEndUtc), w.PastDueSubject,
                        string.Format(w.PastDueText, number, BillingText.Date(after.GraceEndsUtc ?? utcNow, lang))),
                    "trial-ended" => await _mailer.SendOnceAsync(owner, "trial-ended", SubscriptionRules.Key(sub.TrialEndsUtc), w.TrialEndedSubject, w.TrialEndedText),
                    "read-only" => await _mailer.SendOnceAsync(owner, "read-only", SubscriptionRules.Key(sub.CurrentPeriodEndUtc), w.ReadOnlySubject, w.ReadOnlyText),
                    "cancelled" => await _mailer.SendOnceAsync(owner, "cancelled", SubscriptionRules.Key(sub.CurrentPeriodEndUtc), w.CancelledSubject, w.CancelledText),
                    _ => false
                };
                if (sent) emails++;
            }

            // 2. Reminders: the trial's end, and the last days of grace.
            if (SubscriptionRules.TrialReminder(sub.Status == SubscriptionStatus.Trialing ? sub.TrialEndsUtc : null, utcNow) is { } trialKind)
            {
                var days = (int)Math.Ceiling((sub.TrialEndsUtc!.Value - utcNow).TotalDays);
                if (await _mailer.SendOnceAsync(owner, trialKind, SubscriptionRules.Key(sub.TrialEndsUtc),
                        days <= 1 ? w.TrialSubjectOne : string.Format(w.TrialSubject, days),
                        string.Format(w.TrialText, BillingText.LastDay(sub.TrialEndsUtc.Value, lang)))) emails++;
            }
            if (sub.Status == SubscriptionStatus.PastDue && SubscriptionRules.GraceReminder(sub.GraceEndsUtc, utcNow) is { } graceKind)
            {
                var open = await _db.Invoices.AsNoTracking().Where(i => i.SubscriptionId == sub.Id && i.Status == InvoiceStatus.Open).Select(i => i.Number).FirstOrDefaultAsync(ct);
                if (await _mailer.SendOnceAsync(owner, graceKind, SubscriptionRules.Key(sub.GraceEndsUtc),
                        string.Format(w.GraceSubject, BillingText.Date(sub.GraceEndsUtc!.Value, lang)),
                        string.Format(w.GraceText, open ?? "-", BillingText.Date(sub.GraceEndsUtc.Value, lang)))) emails++;
            }

            // 3. Renewal: an invoice for the next period, issued the lead days before this one ends.
            if (settings.CanInvoice && sub.Provider == BillingProvider.BankTransfer
                && SubscriptionRules.RenewalDue(BillingService.State(sub), utcNow, settings.RenewalLeadDays)
                && !await _db.Invoices.AnyAsync(i => i.SubscriptionId == sub.Id && i.PeriodStartUtc == sub.CurrentPeriodEndUtc && i.Status != InvoiceStatus.Void, ct))
            {
                var due = sub.CurrentPeriodEndUtc!.Value > utcNow ? sub.CurrentPeriodEndUtc.Value : utcNow.AddDays(Math.Max(1, settings.InvoiceDueDays));
                var (inv, problem) = await _billing.IssueAsync(sub, owner, sub.CurrentPeriodEndUtc.Value, "renewal", due, utcNow);
                if (inv != null) issued++;
                else problems.Add($"{owner.Email}: renewal not issued ({problem})");
            }
        }

        // 4. Invoice emails: each open invoice once (with its PDF), and a reminder before it's due.
        var openInvoices = await _db.Invoices.Include(i => i.Lines).Include(i => i.Owner)
            .Where(i => i.Status == InvoiceStatus.Open).ToListAsync(ct);
        foreach (var inv in openInvoices)
        {
            var owner = inv.Owner!;
            if (await _mailer.SendInvoiceAsync(owner, inv)) emails++;
            if (SubscriptionRules.InvoiceReminder(inv.DueUtc, inv.IssuedUtc, utcNow) is { } dueKind)
            {
                var w = BillingText.For(inv.Language);
                if (await _mailer.SendOnceAsync(owner, dueKind, inv.Number, string.Format(w.DueSoonSubject, inv.Number, BillingText.Date(inv.DueUtc, inv.Language)),
                        string.Format(w.DueSoonText, inv.Number, PricingRules.Money(inv.TotalCents, inv.Currency), BillingText.Date(inv.DueUtc, inv.Language)), inv.BuyerEmail)) emails++;
            }
        }

        if (!_mailer.CanSend && (changes > 0 || issued > 0)) problems.Add("Email isn't set up: owners weren't told.");
        return (changes, issued, emails);
    }
}

/// <summary>Runs the billing inbox every 30 seconds and the lifecycle every minute (BillingRunner).</summary>
public class BillingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BillingWorker> _logger;

    public BillingWorker(IServiceScopeFactory scopes, ILogger<BillingWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            var tick = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<BillingRunner>();
                    if (tick++ % 2 == 0)
                    {
                        var result = await runner.RunAsync(DateTime.UtcNow, stoppingToken);
                        if (result.Events + result.StatusChanges + result.Invoices + result.Emails + result.Problems.Count > 0)
                            _logger.LogInformation("Billing run: {Result}", result);
                    }
                    else
                    {
                        var n = await runner.ProcessEventsAsync(DateTime.UtcNow, stoppingToken);
                        if (n > 0) _logger.LogInformation("Billing: applied {Count} payment event(s)", n);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Billing run failed; trying again shortly");
                }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
