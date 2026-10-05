using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Billing emails to owners, each at most once per (owner, kind, period): the BillingEmailLog row
/// is written first (its unique index decides who sends), and removed again if sending fails,
/// so the next run tries again. Nothing is held in memory, so restarts never lose or repeat one.
/// Sent in the owner's language (en, sq).
/// </summary>
public class BillingMailer
{
    private readonly ApplicationDbContext _db;
    private readonly EmailService _email;
    private readonly SeoService _seo;
    private readonly ILogger<BillingMailer> _logger;

    public BillingMailer(ApplicationDbContext db, EmailService email, SeoService seo, ILogger<BillingMailer> logger)
    {
        _db = db;
        _email = email;
        _seo = seo;
        _logger = logger;
    }

    public bool CanSend => _email.IsConfigured;

    /// <summary>
    /// Sends one billing email unless it went out before. True when it was sent now.
    /// <paramref name="force"/>: send again even if logged (the admin's "Resend").
    /// </summary>
    public async Task<bool> SendOnceAsync(ApplicationUser owner, string kind, string periodKey, string subject, string text,
        string? to = null, IReadOnlyList<EmailAttachment>? attachments = null, bool force = false)
    {
        if (!_email.IsConfigured || string.IsNullOrEmpty(owner.Email)) return false;

        var log = new BillingEmailLog { OwnerId = owner.Id, Kind = kind, PeriodKey = periodKey, SentUtc = DateTime.UtcNow };
        if (!force)
        {
            _db.BillingEmailLog.Add(log);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _db.Entry(log).State = EntityState.Detached;
                return false; // already sent (or being sent by another run)
            }
        }

        var w = BillingText.For(owner.Language);
        var (html, plain) = EmailTemplate.Render(owner.FullName, subject, new[] { text }, w.EmailButton, _seo.Url("/billing"));
        try
        {
            await _email.SendAsync(new EmailMessage(to ?? owner.Email!, owner.FullName, subject, html, plain, attachments));
            _logger.LogInformation("Billing email {Kind} ({Key}) sent to {Email}", kind, periodKey, to ?? owner.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Billing email {Kind} ({Key}) to {Email} failed; it will be retried", kind, periodKey, to ?? owner.Email);
            if (!force)
            {
                _db.BillingEmailLog.Remove(log);
                await _db.SaveChangesAsync();
            }
            return false;
        }
    }

    /// <summary>An invoice with its PDF, to the billing email on it.</summary>
    public Task<bool> SendInvoiceAsync(ApplicationUser owner, Invoice inv, bool force = false)
    {
        var w = BillingText.For(inv.Language);
        var subject = string.Format(w.InvoiceSubject, inv.Number);
        var text = string.Format(w.InvoiceText, inv.Number, PricingRules.Money(inv.TotalCents, inv.Currency, alwaysCents: true),
            BillingText.Date(inv.DueUtc, inv.Language), InvoiceRules.FormatIban(inv.SellerIban));
        var pdf = new EmailAttachment(InvoicePdf.FileName(inv), "application/pdf", InvoicePdf.Render(inv));
        return SendOnceAsync(owner, "invoice", inv.Number, subject, text, inv.BuyerEmail, new[] { pdf }, force);
    }
}
