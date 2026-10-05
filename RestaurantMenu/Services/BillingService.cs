using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>What an owner fills in to be invoiced.</summary>
public record BillingDetails(string LegalName, string? Nipt, string? Address, string? City, string Email);

/// <summary>
/// Money in, for one provider so far: bank transfer. Checkout issues an invoice; the admin marks
/// it paid, which goes through the billing inbox (BillingEvent) exactly like a card provider's
/// webhook will, and only the inbox's processing changes the subscription. The time-based steps
/// (trial end, renewal invoices, past due, read-only, reminders) are BillingWorker's.
/// </summary>
public class BillingService
{
    private readonly ApplicationDbContext _db;
    private readonly PlanSettingsService _settings;
    private readonly BillingMailer _mailer;
    private readonly ILogger<BillingService> _logger;

    public BillingService(ApplicationDbContext db, PlanSettingsService settings, BillingMailer mailer, ILogger<BillingService> logger)
    {
        _db = db;
        _settings = settings;
        _mailer = mailer;
        _logger = logger;
    }

    public static LifecycleState State(Subscription s) =>
        new(s.Status, s.IsLegacy, s.TrialEndsUtc, s.CurrentPeriodStartUtc, s.CurrentPeriodEndUtc, s.GraceEndsUtc, s.CancelAtPeriodEnd);

    private static void Set(Subscription s, LifecycleState x)
    {
        s.Status = x.Status;
        s.IsLegacy = x.IsLegacy;
        s.TrialEndsUtc = x.TrialEndsUtc;
        s.CurrentPeriodStartUtc = x.CurrentPeriodStartUtc;
        s.CurrentPeriodEndUtc = x.CurrentPeriodEndUtc;
        s.GraceEndsUtc = x.GraceEndsUtc;
        s.CancelAtPeriodEnd = x.CancelAtPeriodEnd;
    }

    public Task<BillingProfile?> ProfileAsync(string ownerId) =>
        _db.BillingProfiles.FirstOrDefaultAsync(p => p.OwnerId == ownerId);

    /// <summary>The open (unpaid, not void) invoice an owner should pay now, if any.</summary>
    public Task<Invoice?> OpenInvoiceAsync(string ownerId) =>
        _db.Invoices.Include(i => i.Lines).Where(i => i.OwnerId == ownerId && i.Status == InvoiceStatus.Open)
            .OrderBy(i => i.PeriodStartUtc).FirstOrDefaultAsync();

    /// <summary>
    /// "Get the invoice" (bank transfer): saves the billing details, then issues an invoice for
    /// the period that's next to pay: after a running trial, now after a lapse, or the next
    /// period of an active plan. An existing open invoice is returned instead of a second one.
    /// </summary>
    public async Task<(Invoice? Invoice, string? Problem)> CheckoutAsync(ApplicationUser owner, BillingDetails details,
        BillingInterval interval, DateTime utcNow)
    {
        var w = BillingText.For(owner.Language);
        var settings = await _settings.GetAsync();
        if (!settings.CanInvoice) return (null, w.ErrNotReady);
        var sub = await _db.Subscriptions.Include(s => s.Items).FirstOrDefaultAsync(s => s.OwnerId == owner.Id);
        if (sub == null || sub.IsLegacy) return (null, w.ErrNotReady);

        var profile = await ProfileAsync(owner.Id);
        if (profile == null) _db.BillingProfiles.Add(profile = new BillingProfile { OwnerId = owner.Id, Country = owner.Country });
        profile.LegalName = details.LegalName;
        profile.Nipt = details.Nipt;
        profile.Address = details.Address;
        profile.City = details.City;
        profile.BillingEmail = details.Email;
        profile.UpdatedUtc = utcNow;
        // The period choice is part of the plan for the next invoice.
        if (sub.Interval != interval) sub.NextInterval = interval;
        await _db.SaveChangesAsync();

        if (await OpenInvoiceAsync(owner.Id) is { } open) return (open, null);

        var state = State(sub);
        var renewal = sub.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue && sub.CurrentPeriodEndUtc is { } end && !sub.CancelAtPeriodEnd
                      && EntitlementRules.AccessFor(SubscriptionRules.Advance(state, utcNow, settings.GraceDays).Status) == AccountAccess.Full;
        var start = renewal ? sub.CurrentPeriodEndUtc!.Value : SubscriptionRules.FirstPeriodStart(state, utcNow);
        var due = renewal ? (start > utcNow ? start : utcNow.AddDays(settings.InvoiceDueDays)) : utcNow.AddDays(Math.Max(1, settings.InvoiceDueDays));
        return await IssueAsync(sub, owner, start, renewal ? "renewal" : "first", due, utcNow);
    }

    /// <summary>
    /// Issues an invoice for the subscription's plan (its pending change if any) for the period
    /// starting at <paramref name="start"/>: priced from today's price list, seller and buyer
    /// copied in, the next number of the year. Emailed by BillingWorker (or right away by the caller).
    /// </summary>
    public async Task<(Invoice? Invoice, string? Problem)> IssueAsync(Subscription sub, ApplicationUser owner, DateTime start,
        string kind, DateTime due, DateTime utcNow)
    {
        var w = BillingText.For(owner.Language);
        var settings = await _settings.GetAsync();
        var plan = InvoiceRules.PlanFor(sub.Items.Select(i => i.Module), sub.BranchQuantity, sub.Interval, sub.NextModules, sub.NextBranchQuantity, sub.NextInterval);
        var (lines, unpriced) = InvoiceRules.Lines(plan, await _settings.PriceTableAsync(utcNow), settings.Currency);
        if (unpriced.Count > 0) return (null, w.ErrPrice);

        var profile = await ProfileAsync(owner.Id);
        var subtotal = lines.Sum(l => l.TotalCents!.Value);
        var vat = InvoiceRules.Vat(subtotal, settings.VatPercent);
        var year = utcNow.Year;

        var strategy = _db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                // Atomic: two invoices at once never share a number, and numbers are never reused.
                var seq = (await _db.Database.SqlQuery<int>($"""
                    INSERT INTO "InvoiceSequences" ("Year", "Last") VALUES ({year}, 1)
                    ON CONFLICT ("Year") DO UPDATE SET "Last" = "InvoiceSequences"."Last" + 1
                    RETURNING "Last" AS "Value"
                    """).ToListAsync()).Single(); // not composed: an INSERT can't be wrapped in a SELECT
                var address = string.Join(", ", new[] { profile?.Address, profile?.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var inv = new Invoice
                {
                    Number = InvoiceRules.Number(year, seq), Year = year, Sequence = seq,
                    OwnerId = owner.Id, SubscriptionId = sub.Id,
                    PeriodStartUtc = start, PeriodEndUtc = SubscriptionRules.PeriodEnd(start, plan.Interval),
                    Interval = plan.Interval, BranchQuantity = plan.Branches, Kind = kind,
                    Currency = settings.Currency, SubtotalCents = subtotal, VatPercent = settings.VatPercent, VatCents = vat, TotalCents = subtotal + vat,
                    Status = InvoiceStatus.Open, IssuedUtc = utcNow, DueUtc = due, Provider = BillingProvider.BankTransfer,
                    Language = owner.Language,
                    SellerName = settings.OperatorName!, SellerNipt = settings.OperatorNipt, SellerAddress = settings.OperatorAddress,
                    SellerIban = settings.OperatorIban, SellerBank = settings.OperatorBank, SellerSwift = settings.OperatorSwift,
                    BuyerName = profile?.LegalName ?? owner.FullName, BuyerNipt = profile?.Nipt,
                    BuyerAddress = address.Length == 0 ? null : address, BuyerEmail = profile?.BillingEmail ?? owner.Email!
                };
                foreach (var l in lines)
                    inv.Lines.Add(new InvoiceLine { Module = l.Module, Quantity = l.Quantity, UnitCents = l.UnitCents!.Value, TotalCents = l.TotalCents!.Value });
                _db.Invoices.Add(inv);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger.LogInformation("Billing: invoice {Number} issued to {Email} for {Total} ({Kind})", inv.Number, owner.Email, inv.TotalCents, kind);
                return ((Invoice?)inv, (string?)null);
            });
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same period was invoiced a moment ago (one live invoice per period).
            _db.ChangeTracker.Clear();
            return (await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.SubscriptionId == sub.Id && i.PeriodStartUtc == start && i.Status != InvoiceStatus.Void), null);
        }
    }

    // ---------------------------------------------------------------- the inbox

    private record PaymentPayload(int InvoiceId, DateTime PaidUtc, string? Note, string? ActorId);

    /// <summary>
    /// The admin received a bank transfer: stored in the billing inbox (a repeat is a no-op), then
    /// applied by the same processing a card provider's webhook will go through.
    /// </summary>
    public async Task<bool> MarkPaidAsync(int invoiceId, DateTime paidUtc, string? note, string? actorId)
    {
        var inv = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (inv == null || inv.Status != InvoiceStatus.Open) return false;
        _db.BillingEvents.Add(new BillingEvent
        {
            Provider = BillingProvider.BankTransfer,
            EventId = $"invoice-{invoiceId}-paid",
            Type = BillingEventType.PaymentSucceeded,
            PayloadJson = JsonSerializer.Serialize(new PaymentPayload(invoiceId, paidUtc, note, actorId)),
            ReceivedUtc = DateTime.UtcNow
        });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear(); // already recorded
        }
        return true;
    }

    /// <summary>
    /// Applies one inbox event inside the caller's transaction. Idempotent: a paid invoice stays
    /// paid and the subscription isn't moved twice. Returns the invoice it just paid (null if
    /// nothing changed). Throws on anything unexpected (the inbox retries).
    /// </summary>
    public async Task<Invoice?> ApplyAsync(BillingEvent e, DateTime utcNow)
    {
        if (e.Type != BillingEventType.PaymentSucceeded) return null; // other types arrive with card providers
        var p = JsonSerializer.Deserialize<PaymentPayload>(e.PayloadJson) ?? throw new InvalidOperationException("Empty payload");
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == p.InvoiceId)
                  ?? throw new InvalidOperationException($"Invoice {p.InvoiceId} not found");
        if (inv.Status == InvoiceStatus.Paid) return null;
        if (inv.Status == InvoiceStatus.Void) throw new InvalidOperationException($"Invoice {inv.Number} is void");

        inv.Status = InvoiceStatus.Paid;
        inv.PaidUtc = p.PaidUtc;
        inv.PaidNote = p.Note;

        var sub = await _db.Subscriptions.IgnoreQueryFilters().Include(s => s.Items).FirstAsync(s => s.Id == inv.SubscriptionId);
        var before = SubscriptionService.Describe(sub);
        Set(sub, SubscriptionRules.ApplyPayment(State(sub), inv.PeriodStartUtc, inv.PeriodEndUtc));
        sub.Provider = BillingProvider.BankTransfer;
        sub.Interval = inv.Interval;
        sub.BranchQuantity = inv.BranchQuantity;
        // What was paid for is what the account has now: the invoice's modules at its prices.
        var paidModules = inv.Lines.Select(l => l.Module).ToHashSet();
        foreach (var gone in sub.Items.Where(i => !paidModules.Contains(i.Module)).ToList()) sub.Items.Remove(gone);
        foreach (var line in inv.Lines)
        {
            var item = sub.Items.FirstOrDefault(i => i.Module == line.Module);
            if (item == null) sub.Items.Add(item = new SubscriptionItem { Module = line.Module });
            item.Quantity = line.Quantity;
            item.UnitAmountCents = line.UnitCents;
        }
        sub.NextModules = null;
        sub.NextBranchQuantity = null;
        sub.NextInterval = null;
        sub.UpdatedUtc = utcNow;
        var owner = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inv.OwnerId);
        owner.NumberOfBranches = sub.BranchQuantity;

        _db.SubscriptionAudits.Add(new SubscriptionAudit
        {
            SubscriptionId = sub.Id, ActorId = p.ActorId, Action = $"paid {inv.Number}", FromJson = before,
            ToJson = SubscriptionService.Describe(sub), AtUtc = utcNow
        });
        return inv;
    }

    /// <summary>After a payment is applied: the "payment received" email (once).</summary>
    public async Task ThankAsync(Invoice inv)
    {
        var owner = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inv.OwnerId);
        var sub = await _db.Subscriptions.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == inv.SubscriptionId);
        var w = BillingText.For(owner.Language);
        await _mailer.SendOnceAsync(owner, "paid", inv.Number, w.PaidSubject,
            string.Format(w.PaidText, inv.Number, PricingRules.Money(inv.TotalCents, inv.Currency),
                BillingText.LastDay(sub.CurrentPeriodEndUtc ?? inv.PeriodEndUtc, owner.Language)), inv.BuyerEmail);
    }

    /// <summary>Cancels an unpaid invoice (a mistake, or a plan that changed). Paid ones can't be voided.</summary>
    public async Task<bool> VoidAsync(int invoiceId, string? actorId, DateTime utcNow)
    {
        var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (inv == null || inv.Status != InvoiceStatus.Open) return false;
        inv.Status = InvoiceStatus.Void;
        inv.VoidedUtc = utcNow;
        _db.SubscriptionAudits.Add(new SubscriptionAudit { SubscriptionId = inv.SubscriptionId, ActorId = actorId, Action = $"voided {inv.Number}", AtUtc = utcNow });
        await _db.SaveChangesAsync();
        return true;
    }

    // ---------------------------------------------------------------- owner's changes

    /// <summary>
    /// The owner changes modules, branches or the billing period. With nothing paid yet (a trial)
    /// it applies at once; otherwise it's stored for the next period and billed on its invoice.
    /// Returns true when it applied at once.
    /// </summary>
    public async Task<bool> ChangePlanAsync(string ownerId, PlanSelection plan, DateTime utcNow)
    {
        var sub = await _db.Subscriptions.Include(s => s.Items).FirstAsync(s => s.OwnerId == ownerId);
        var before = SubscriptionService.Describe(sub);
        var now = sub.CurrentPeriodEndUtc == null && sub.Status == SubscriptionStatus.Trialing;
        if (now)
        {
            foreach (var gone in sub.Items.Where(i => !plan.Modules.Contains(i.Module)).ToList()) sub.Items.Remove(gone);
            foreach (var m in plan.Modules)
            {
                var item = sub.Items.FirstOrDefault(i => i.Module == m);
                if (item == null) sub.Items.Add(item = new SubscriptionItem { Module = m });
                item.Quantity = EntitlementRules.IsPerBranch(m) ? plan.Branches : 1;
            }
            sub.BranchQuantity = plan.Branches;
            sub.Interval = plan.Interval;
            (await _db.Users.FirstAsync(u => u.Id == ownerId)).NumberOfBranches = plan.Branches;
        }
        else
        {
            sub.NextModules = string.Join(",", plan.Modules.Where(m => m != BillingModule.Menu).OrderBy(m => m));
            sub.NextBranchQuantity = plan.Branches;
            sub.NextInterval = plan.Interval;
        }
        sub.UpdatedUtc = utcNow;
        _db.SubscriptionAudits.Add(new SubscriptionAudit
        {
            SubscriptionId = sub.Id, ActorId = ownerId, Action = now ? "owner changed plan" : "owner changed next period",
            FromJson = before, ToJson = SubscriptionService.Describe(sub) + (now ? "" : $" next: {sub.NextModules} ×{sub.NextBranchQuantity} {sub.NextInterval}"), AtUtc = utcNow
        });
        // An open invoice for the next period no longer matches: void it, the worker issues a new one.
        if (!now)
        {
            foreach (var stale in await _db.Invoices.Where(i => i.SubscriptionId == sub.Id && i.Status == InvoiceStatus.Open && i.Kind == "renewal").ToListAsync())
            {
                stale.Status = InvoiceStatus.Void;
                stale.VoidedUtc = utcNow;
            }
        }
        await _db.SaveChangesAsync();
        return now;
    }

    public async Task UndoNextChangeAsync(string ownerId, DateTime utcNow)
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.OwnerId == ownerId);
        sub.NextModules = null;
        sub.NextBranchQuantity = null;
        sub.NextInterval = null;
        sub.UpdatedUtc = utcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Stops (or resumes) renewing at the end of the paid period; an issued renewal invoice is voided.</summary>
    public async Task<bool> CancelAsync(string ownerId, bool cancel, DateTime utcNow)
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.OwnerId == ownerId);
        if (sub.IsLegacy || sub.CurrentPeriodEndUtc == null || sub.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue)) return false;
        sub.CancelAtPeriodEnd = cancel;
        sub.UpdatedUtc = utcNow;
        if (cancel)
        {
            foreach (var renewal in await _db.Invoices.Where(i => i.SubscriptionId == sub.Id && i.Status == InvoiceStatus.Open && i.PeriodStartUtc >= sub.CurrentPeriodEndUtc).ToListAsync())
            {
                renewal.Status = InvoiceStatus.Void;
                renewal.VoidedUtc = utcNow;
            }
        }
        _db.SubscriptionAudits.Add(new SubscriptionAudit { SubscriptionId = sub.Id, ActorId = ownerId, Action = cancel ? "owner cancelled at period end" : "owner resumed", AtUtc = utcNow });
        await _db.SaveChangesAsync();
        return true;
    }
}
