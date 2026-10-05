using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>The /billing page: what the owner's plan is, what it costs and how they pay.</summary>
public record BillingPage(
    BillingText.Words W,
    string Lang,
    Subscription Sub,
    Entitlements Now,
    PlanSelection Current,
    Quote CurrentQuote,
    PlanSelection? Next,
    Invoice? Open,
    IReadOnlyList<Invoice> Invoices,
    BillingDetailsForm Details,
    PlanSettings Settings,
    bool CanPay,
    IReadOnlyList<string>? Errors);

/// <summary>The bank-transfer checkout form (billing details and the period).</summary>
public class BillingDetailsForm
{
    public string? LegalName { get; set; }
    public string? Nipt { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public BillingInterval Interval { get; set; } = BillingInterval.Month;
}

/// <summary>
/// The owner's billing, in their language (en, sq): plan and status, paying by bank transfer
/// (an invoice with the IBAN and a reference), the open invoice, all invoices as PDFs, changes for
/// the next period, and cancelling. Reachable when the account is read-only (AccountGateFilter
/// leaves it open), since that's where it gets fixed.
/// </summary>
[Authorize(Roles = "OWNER")]
[NoIndex]
[Route("billing")]
public class BillingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly BillingService _billing;
    private readonly BillingMailer _mailer;
    private readonly PlanSettingsService _settings;
    private readonly IEntitlementService _entitlements;

    public BillingController(ApplicationDbContext db, UserManager<ApplicationUser> users, BillingService billing, BillingMailer mailer,
        PlanSettingsService settings, IEntitlementService entitlements)
    {
        _db = db;
        _users = users;
        _billing = billing;
        _mailer = mailer;
        _settings = settings;
        _entitlements = entitlements;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var owner = (await _users.GetUserAsync(User))!;
        var page = await PageAsync(owner, null, null);
        return page == null ? RedirectToAction("Index", "Dashboard") : View(page);
    }

    /// <summary>"Get the invoice": billing details saved, invoice issued and emailed with its PDF.</summary>
    [HttpPost("checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(BillingDetailsForm form)
    {
        var owner = (await _users.GetUserAsync(User))!;
        var w = BillingText.For(owner.Language);
        var errors = new List<string>();
        var name = SignupRules.Clean(form.LegalName);
        var nipt = SignupRules.Clean(form.Nipt).Replace(" ", "").ToUpperInvariant();
        var email = (form.Email ?? "").Trim();
        if (name.Length is < 2 or > 200) errors.Add(w.ErrLegalName);
        // Albanian businesses must give a NIPT (a letter, 8 digits, a letter); elsewhere it's optional.
        if (owner.Country == "AL" ? !System.Text.RegularExpressions.Regex.IsMatch(nipt, "^[A-Z][0-9]{8}[A-Z]$") : nipt.Length > 20) errors.Add(w.ErrNipt);
        if (!SignupRules.IsEmail(email)) errors.Add(w.ErrEmail);
        if (errors.Count > 0) return View("Index", await PageAsync(owner, form, errors));

        var (inv, problem) = await _billing.CheckoutAsync(owner,
            new BillingDetails(name, nipt.Length == 0 ? null : nipt, Trim(form.Address, 300), Trim(form.City, 100), email),
            form.Interval == BillingInterval.Year ? BillingInterval.Year : BillingInterval.Month, DateTime.UtcNow);
        if (inv == null) return View("Index", await PageAsync(owner, form, new List<string> { problem ?? w.ErrNotReady }));

        // Sent now so it's in their inbox at once; the worker would otherwise send it within a minute.
        await _mailer.SendInvoiceAsync(owner, inv);
        TempData["Success"] = string.Format(w.InvoiceCreated, inv.Number, inv.BuyerEmail);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("plan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Plan(List<string>? m, string? b, string? i)
    {
        var owner = (await _users.GetUserAsync(User))!;
        var w = BillingText.For(owner.Language);
        var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == owner.Id);
        if (sub == null || sub.IsLegacy) return RedirectToAction(nameof(Index));
        var plan = PricingRules.Parse(m, b, i);
        var now = await _billing.ChangePlanAsync(owner.Id, plan, DateTime.UtcNow);
        TempData["Success"] = now ? w.ChangeSavedNow
            : string.Format(w.ChangeSavedNext, BillingText.Date(sub.CurrentPeriodEndUtc ?? DateTime.UtcNow, owner.Language));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("plan/undo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoPlan()
    {
        var owner = (await _users.GetUserAsync(User))!;
        await _billing.UndoNextChangeAsync(owner.Id, DateTime.UtcNow);
        TempData["Success"] = BillingText.For(owner.Language).ChangeUndone;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(bool resume = false)
    {
        var owner = (await _users.GetUserAsync(User))!;
        var w = BillingText.For(owner.Language);
        if (await _billing.CancelAsync(owner.Id, !resume, DateTime.UtcNow))
        {
            var end = (await _db.Subscriptions.AsNoTracking().FirstAsync(s => s.OwnerId == owner.Id)).CurrentPeriodEndUtc!.Value;
            TempData["Success"] = resume ? w.Resumed : string.Format(w.CancelDone, BillingText.LastDay(end, owner.Language));
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>One of the owner's own invoices as a PDF.</summary>
    [HttpGet("invoices/{id:int}.pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var ownerId = _users.GetUserId(User);
        var inv = await _db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id && i.OwnerId == ownerId);
        if (inv == null) return NotFound();
        return File(InvoicePdf.Render(inv), "application/pdf", InvoicePdf.FileName(inv));
    }

    private async Task<BillingPage?> PageAsync(ApplicationUser owner, BillingDetailsForm? form, List<string>? errors)
    {
        var sub = await _db.Subscriptions.AsNoTracking().Include(s => s.Items).FirstOrDefaultAsync(s => s.OwnerId == owner.Id);
        if (sub == null) return null;
        var w = BillingText.For(owner.Language);
        var settings = await _settings.GetAsync();
        var now = await _entitlements.ForOwnerAsync(owner.Id);
        var prices = await _settings.PriceTableAsync(DateTime.UtcNow);

        var current = InvoiceRules.PlanFor(sub.Items.Select(i => i.Module), sub.BranchQuantity, sub.Interval, null, null, null);
        var next = sub.NextModules != null || sub.NextBranchQuantity != null || sub.NextInterval != null
            ? InvoiceRules.PlanFor(sub.Items.Select(i => i.Module), sub.BranchQuantity, sub.Interval, sub.NextModules, sub.NextBranchQuantity, sub.NextInterval)
            : null;
        var invoices = await _db.Invoices.AsNoTracking().Include(i => i.Lines).Where(i => i.OwnerId == owner.Id)
            .OrderByDescending(i => i.IssuedUtc).Take(50).ToListAsync();
        var open = invoices.Where(i => i.Status == InvoiceStatus.Open).OrderBy(i => i.PeriodStartUtc).FirstOrDefault();

        var profile = await _billing.ProfileAsync(owner.Id);
        form ??= new BillingDetailsForm
        {
            LegalName = profile?.LegalName ?? owner.SignupRestaurantName,
            Nipt = profile?.Nipt ?? (string.IsNullOrEmpty(owner.NIPT) ? null : owner.NIPT),
            Address = profile?.Address,
            City = profile?.City,
            Email = profile?.BillingEmail ?? owner.Email,
            Interval = sub.NextInterval ?? sub.Interval
        };
        var canPay = !sub.IsLegacy && open == null && (now.Status == SubscriptionStatus.Trialing || !now.CanWrite);

        ViewData["Title"] = w.Title;
        return new BillingPage(w, owner.Language, sub, now, current, PricingRules.Quote(current, prices, settings.Currency), next, open, invoices,
            form, settings, canPay, errors);
    }

    private static string? Trim(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();
}
