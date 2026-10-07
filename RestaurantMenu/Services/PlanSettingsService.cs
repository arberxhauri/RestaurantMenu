using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>The plan settings form (Admin → Plans &amp; prices).</summary>
public record PlanSettingsInput(string Currency, int TrialDays, int SeatsPerBranch, int GraceDays, bool SignupEnabled, bool SignupRequireApproval,
    InvoiceSettingsInput Invoicing);

/// <summary>The seller on invoices and the invoicing rules (phase 3).</summary>
public record InvoiceSettingsInput(string? Name, string? Nipt, string? Address, string? Email, string? Iban, string? Bank, string? Swift,
    decimal VatPercent, int DueDays, int RenewalLeadDays, bool CardPaymentsEnabled = false);

/// <summary>
/// The platform's plan settings and price book, both in the database and edited in Admin → Plans
/// &amp; prices. Everything that needs the currency, trial length, seats, grace days, the signup
/// switches or a price asks here. Loaded once per request (scoped), never cached across requests,
/// so a change applies on the next click and on every instance.
/// Configuration (Billing__…, Signup__…) is only the seed for the very first start.
/// </summary>
public class PlanSettingsService
{
    private readonly ApplicationDbContext _db;
    private readonly BillingOptions _billingSeed;
    private readonly SignupOptions _signupSeed;
    private PlanSettings? _settings;

    public PlanSettingsService(ApplicationDbContext db, IOptions<BillingOptions> billing, IOptions<SignupOptions> signup)
    {
        _db = db;
        _billingSeed = billing.Value;
        _signupSeed = signup.Value;
    }

    /// <summary>The settings row (or, before the first start has created it, the configured seed).</summary>
    public async Task<PlanSettings> GetAsync() =>
        _settings ??= await _db.PlanSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlanSettings.SingletonId)
                      ?? Seed(_billingSeed, _signupSeed, DateTime.UtcNow);

    public async Task<BillingDefaults> DefaultsAsync()
    {
        var s = await GetAsync();
        return new BillingDefaults(Math.Max(0, s.SeatsPerBranch), Math.Max(0, s.GraceDays));
    }

    public static PlanSettings Seed(BillingOptions b, SignupOptions s, DateTime utcNow) => new()
    {
        Currency = string.IsNullOrWhiteSpace(b.Currency) ? "EUR" : b.Currency.Trim().ToUpperInvariant(),
        TrialDays = Math.Clamp(b.TrialDays, 1, 365),
        SeatsPerBranch = Math.Clamp(b.SeatsPerBranch, 0, 100),
        GraceDays = Math.Clamp(b.GraceDays, 0, 90),
        SignupEnabled = s.Enabled,
        SignupRequireApproval = s.RequireApproval,
        UpdatedUtc = utcNow
    };

    /// <summary>Saves the settings. Null when saved; otherwise why not (someone else saved first).</summary>
    public async Task<string?> SaveAsync(PlanSettingsInput input, uint? expectedVersion, string? actorId, DateTime utcNow)
    {
        var row = await _db.PlanSettings.FirstOrDefaultAsync(s => s.Id == PlanSettings.SingletonId);
        if (row == null)
        {
            row = Seed(_billingSeed, _signupSeed, utcNow);
            _db.PlanSettings.Add(row);
        }
        else if (expectedVersion is { } v)
        {
            _db.Entry(row).Property(x => x.Version).OriginalValue = v;
        }
        row.Currency = input.Currency.Trim().ToUpperInvariant();
        row.TrialDays = Math.Clamp(input.TrialDays, 1, 365);
        row.SeatsPerBranch = Math.Clamp(input.SeatsPerBranch, 0, 100);
        row.GraceDays = Math.Clamp(input.GraceDays, 0, 90);
        row.SignupEnabled = input.SignupEnabled;
        row.SignupRequireApproval = input.SignupRequireApproval;
        static string? Clean(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();
        var inv = input.Invoicing;
        row.OperatorName = Clean(inv.Name, 200);
        row.OperatorNipt = Clean(inv.Nipt, 20)?.ToUpperInvariant();
        row.OperatorAddress = Clean(inv.Address, 300);
        row.OperatorEmail = Clean(inv.Email, 256);
        row.OperatorIban = Clean(inv.Iban, 40)?.Replace(" ", "").ToUpperInvariant();
        row.OperatorBank = Clean(inv.Bank, 120);
        row.OperatorSwift = Clean(inv.Swift, 11)?.ToUpperInvariant();
        row.VatPercent = Math.Clamp(inv.VatPercent, 0, 50);
        row.InvoiceDueDays = Math.Clamp(inv.DueDays, 1, 90);
        row.RenewalLeadDays = Math.Clamp(inv.RenewalLeadDays, 0, 60);
        row.CardPaymentsEnabled = inv.CardPaymentsEnabled;
        row.UpdatedUtc = utcNow;
        row.UpdatedById = actorId;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return "Someone else saved these settings at the same time. Reload the page and try again.";
        }
        _settings = null;
        return null;
    }

    /// <summary>The unit price in force for each module (latest row up to now, unless withdrawn), in cents.</summary>
    public async Task<Dictionary<BillingModule, int>> CurrentPricesAsync(string currency, BillingInterval interval, DateTime utcNow)
    {
        var rows = await _db.PriceBook.AsNoTracking()
            .Where(p => p.Currency == currency && p.Interval == interval && p.ValidFromUtc <= utcNow)
            .ToListAsync();
        return rows.GroupBy(p => p.Module)
            .Select(g => g.OrderByDescending(p => p.ValidFromUtc).ThenByDescending(p => p.Id).First())
            .Where(p => !p.Withdrawn)
            .ToDictionary(p => p.Module, p => p.UnitAmountCents);
    }

    /// <summary>Current unit prices for both intervals in the settings' currency, for quotes (PricingRules).</summary>
    public async Task<Dictionary<(BillingModule, BillingInterval), int>> PriceTableAsync(DateTime utcNow)
    {
        var currency = (await GetAsync()).Currency;
        var table = new Dictionary<(BillingModule, BillingInterval), int>();
        foreach (var interval in Enum.GetValues<BillingInterval>())
            foreach (var (module, cents) in await CurrentPricesAsync(currency, interval, utcNow))
                table[(module, interval)] = cents;
        return table;
    }

    /// <summary>
    /// Sets the prices for the settings' currency: a new dated row for each one that changed, a
    /// "withdrawn" row for each one that was cleared. Unchanged prices add nothing. Existing
    /// subscriptions keep the price they were sold at. Returns how many prices changed.
    /// </summary>
    public async Task<int> SetPricesAsync(IReadOnlyDictionary<(BillingModule, BillingInterval), int?> wanted, string? actorId, DateTime utcNow)
    {
        var currency = (await GetAsync()).Currency;
        var current = await PriceTableAsync(utcNow);
        var changed = 0;
        foreach (var ((module, interval), cents) in wanted)
        {
            var had = current.TryGetValue((module, interval), out var old) ? old : (int?)null;
            if (had == cents) continue;
            _db.PriceBook.Add(new PriceBook
            {
                Module = module, Interval = interval, Currency = currency,
                UnitAmountCents = cents ?? 0, Withdrawn = cents == null,
                ValidFromUtc = utcNow, ChangedById = actorId
            });
            changed++;
        }
        if (changed > 0) await _db.SaveChangesAsync();
        return changed;
    }

    /// <summary>The latest price changes, newest first, for the admin page.</summary>
    public Task<List<PriceBook>> HistoryAsync(int take) =>
        _db.PriceBook.AsNoTracking().OrderByDescending(p => p.ValidFromUtc).ThenByDescending(p => p.Id).Take(take).ToListAsync();
}
