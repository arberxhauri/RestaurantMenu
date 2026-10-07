using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>What applying one inbox event changed, for what happens after its transaction commits.</summary>
public record ApplyResult(Invoice? Paid = null, string? OwnerId = null, string? StatusEmail = null)
{
    public static readonly ApplyResult None = new();
}

/// <summary>
/// Card payments through Paddle (merchant of record: Paddle sells, charges VAT and issues the
/// receipt; the app keeps the subscription state). Prices are mirrored from the price book, a
/// card checkout is a Paddle transaction for the plan, and Paddle's webhooks, stored in the
/// billing inbox, move the subscription: paid, renewed, past due, cancelled, changed.
/// </summary>
public class PaddleBilling
{
    private readonly ApplicationDbContext _db;
    private readonly PaddleClient _paddle;
    private readonly PlanSettingsService _settings;
    private readonly ILogger<PaddleBilling> _logger;

    public PaddleBilling(ApplicationDbContext db, PaddleClient paddle, PlanSettingsService settings, ILogger<PaddleBilling> logger)
    {
        _db = db;
        _paddle = paddle;
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => _paddle.IsConfigured;

    /// <summary>
    /// The Paddle account in use: sandbox or production. Ids from the other one (prices,
    /// products, customers, subscriptions) mean nothing here and are ignored, so switching from
    /// the sandbox to the live account syncs fresh prices and starts card plans afresh.
    /// </summary>
    public string Env => _paddle.EnvironmentKey;

    /// <summary>The subscription is a card plan in the Paddle account in use.</summary>
    public bool IsCurrent(Subscription s) =>
        s.Provider == BillingProvider.Paddle && s.ProviderSubscriptionId != null && s.ProviderEnvironment == Env;

    /// <summary>Card payments can be offered: Paddle is set up and switched on in Plans &amp; prices.</summary>
    public async Task<bool> IsOfferedAsync() => _paddle.IsConfigured && (await _settings.GetAsync()).CardPaymentsEnabled;

    // ---------------------------------------------------------------- prices

    /// <summary>
    /// Mirrors today's price list into Paddle: a product per module (once), a Paddle price for
    /// every current price that has none, and archives Paddle prices that a newer one replaced
    /// (subscriptions on them keep renewing at their price until changed). Returns what it did.
    /// </summary>
    public async Task<(int Created, int Archived)> SyncPricesAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var currency = (await _settings.GetAsync()).Currency;
        var rows = await _db.PriceBook.Where(p => p.Currency == currency && p.ValidFromUtc <= utcNow).ToListAsync(ct);
        var current = rows.GroupBy(p => (p.Module, p.Interval))
            .Select(g => g.OrderByDescending(p => p.ValidFromUtc).ThenByDescending(p => p.Id).First()).ToList();
        var env = Env;
        var products = await _db.PaddleProducts.Where(p => p.Environment == env).ToDictionaryAsync(p => p.Module, p => p.ProductId, ct);
        int created = 0, archived = 0;

        // A price is mirrored here only if its Paddle id is from this account (a sandbox id isn't live).
        foreach (var row in current.Where(r => !r.Withdrawn && !(r.PaddlePriceId != null && r.PaddleEnvironment == env)))
        {
            if (!products.TryGetValue(row.Module, out var productId))
            {
                productId = await _paddle.CreateProductAsync($"My Quick Menu: {EntitlementRules.Name(row.Module)}", ct);
                _db.PaddleProducts.Add(new PaddleProduct { Module = row.Module, Environment = env, ProductId = productId });
                products[row.Module] = productId;
                await _db.SaveChangesAsync(ct);
            }
            var description = $"{EntitlementRules.Name(row.Module)}, {(row.Interval == BillingInterval.Year ? "yearly" : "monthly")}" +
                              (EntitlementRules.IsPerBranch(row.Module) ? ", per branch" : "");
            row.PaddlePriceId = await _paddle.CreatePriceAsync(productId, description, row.UnitAmountCents, row.Currency, row.Interval == BillingInterval.Year, ct);
            row.PaddleEnvironment = env;
            row.PaddleArchivedUtc = null;
            await _db.SaveChangesAsync(ct); // one at a time: a failure halfway keeps what was made
            created++;
        }

        var currentIds = current.Select(r => r.Id).ToHashSet();
        foreach (var old in rows.Where(r => r.PaddlePriceId != null && r.PaddleEnvironment == env && r.PaddleArchivedUtc == null && (!currentIds.Contains(r.Id) || r.Withdrawn)))
        {
            await _paddle.ArchivePriceAsync(old.PaddlePriceId!, ct);
            old.PaddleArchivedUtc = utcNow;
            await _db.SaveChangesAsync(ct);
            archived++;
        }
        if (created + archived > 0) _logger.LogInformation("Paddle: price sync created {Created}, archived {Archived}", created, archived);
        return (created, archived);
    }

    /// <summary>The Paddle price and quantity for each module of a plan; null when a module has no price.</summary>
    private async Task<List<(string PriceId, int Quantity)>?> ItemsForAsync(PlanSelection plan, DateTime utcNow, CancellationToken ct)
    {
        await SyncPricesAsync(utcNow, ct);
        var currency = (await _settings.GetAsync()).Currency;
        var rows = await _db.PriceBook.AsNoTracking()
            .Where(p => p.Currency == currency && p.Interval == plan.Interval && p.ValidFromUtc <= utcNow).ToListAsync(ct);
        var items = new List<(string, int)>();
        foreach (var m in plan.Modules.OrderBy(m => m))
        {
            var row = rows.Where(r => r.Module == m).OrderByDescending(r => r.ValidFromUtc).ThenByDescending(r => r.Id).FirstOrDefault();
            if (row == null || row.Withdrawn || row.PaddlePriceId == null || row.PaddleEnvironment != Env) return null;
            items.Add((row.PaddlePriceId, EntitlementRules.IsPerBranch(m) ? plan.Branches : 1));
        }
        return items;
    }

    // ---------------------------------------------------------------- owner actions

    /// <summary>
    /// A card checkout for the owner's plan (its pending change if any) at the chosen period:
    /// returns the Paddle transaction id that /billing/pay opens, or a problem to show.
    /// </summary>
    public async Task<(string? TransactionId, string? Problem)> StartCheckoutAsync(ApplicationUser owner, BillingInterval interval, DateTime utcNow, CancellationToken ct = default)
    {
        var w = BillingText.For(owner.Language);
        if (!await IsOfferedAsync()) return (null, w.ErrCard);
        var sub = await _db.Subscriptions.Include(s => s.Items).FirstAsync(s => s.OwnerId == owner.Id, ct);
        var plan = InvoiceRules.PlanFor(sub.Items.Select(i => i.Module), sub.BranchQuantity, sub.Interval, sub.NextModules, sub.NextBranchQuantity, interval);
        try
        {
            var items = await ItemsForAsync(plan, utcNow, ct);
            if (items == null) return (null, w.ErrPrice);
            // A customer from the other Paddle account (sandbox while live, or back) isn't known here.
            if (sub.ProviderEnvironment != Env)
            {
                sub.ProviderCustomerId = null;
                if (sub.Provider == BillingProvider.Paddle) sub.ProviderSubscriptionId = null;
                sub.ProviderEnvironment = Env;
            }
            sub.ProviderCustomerId ??= await _paddle.CustomerAsync(owner.Email!, owner.FullName, ct);
            await _db.SaveChangesAsync(ct);
            return (await _paddle.CreateTransactionAsync(sub.ProviderCustomerId, items, owner.Id, ct), null);
        }
        catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Paddle: checkout for {Email} could not start", owner.Email);
            return (null, w.ErrCard);
        }
    }

    public async Task<string?> PortalUrlAsync(string ownerId, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == ownerId, ct);
        if (sub?.ProviderCustomerId == null || !_paddle.IsConfigured || sub.ProviderEnvironment != Env) return null;
        try { return await _paddle.PortalUrlAsync(sub.ProviderCustomerId, sub.ProviderSubscriptionId, ct); }
        catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Paddle: no portal link for {Owner}", ownerId);
            return null;
        }
    }

    /// <summary>
    /// A plan change on a card subscription: sent to Paddle now, prorated (the difference for the
    /// rest of the period is charged or credited). The subscription.updated webhook then updates
    /// the account. False when Paddle refused it.
    /// </summary>
    public async Task<bool> ChangeItemsAsync(Subscription sub, PlanSelection plan, DateTime utcNow, CancellationToken ct = default)
    {
        if (!IsCurrent(sub)) return false;
        try
        {
            var items = await ItemsForAsync(plan with { Interval = sub.Interval }, utcNow, ct);
            if (items == null) return false;
            await _paddle.UpdateItemsAsync(sub.ProviderSubscriptionId, items, ct);
            return true;
        }
        catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Paddle: plan change for {Sub} refused", sub.ProviderSubscriptionId);
            return false;
        }
    }

    public async Task<bool> CancelAsync(Subscription sub, bool cancel, CancellationToken ct = default)
    {
        if (!IsCurrent(sub)) return false;
        try
        {
            if (cancel) await _paddle.CancelAtPeriodEndAsync(sub.ProviderSubscriptionId, ct);
            else await _paddle.ResumeAsync(sub.ProviderSubscriptionId, ct);
            return true;
        }
        catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Paddle: cancel/resume for {Sub} refused", sub.ProviderSubscriptionId);
            return false;
        }
    }

    public async Task<string?> InvoiceUrlAsync(string transactionId, CancellationToken ct = default)
    {
        try { return await _paddle.InvoiceUrlAsync(transactionId, ct); }
        catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException) { return null; }
    }

    // ---------------------------------------------------------------- webhooks

    /// <summary>
    /// Applies one Paddle event inside the caller's transaction. Idempotent: a transaction
    /// already mirrored isn't mirrored again, and subscription events older than the last one
    /// applied are skipped, so replays and out-of-order deliveries change nothing.
    /// </summary>
    public async Task<ApplyResult> ApplyAsync(BillingEvent e, DateTime utcNow)
    {
        var ev = PaddleEvents.Parse(e.PayloadJson) ?? throw new InvalidOperationException("Not a Paddle event");
        if (!PaddleEvents.Handled.Contains(ev.EventType)) return ApplyResult.None;

        var sub = await FindSubscriptionAsync(ev) ?? throw new InvalidOperationException(
            $"No subscription for Paddle {ev.Kind} {ev.ObjectId} (owner {ev.OwnerId ?? "?"}, customer {ev.CustomerId ?? "?"})");
        var settings = await _settings.GetAsync();
        var prices = await _db.PriceBook.AsNoTracking().Where(p => p.PaddlePriceId != null)
            .ToDictionaryAsync(p => p.PaddlePriceId!, p => (p.Module, p.Interval, p.UnitAmountCents));
        var before = SubscriptionService.Describe(sub);

        switch (ev.EventType)
        {
            case "transaction.completed":
            {
                if (ev.PeriodStartUtc is not { } start || ev.PeriodEndUtc is not { } end) return ApplyResult.None; // not a subscription charge
                if (await _db.Invoices.AnyAsync(i => i.Provider == BillingProvider.Paddle && i.ProviderRef == ev.ObjectId)) return ApplyResult.None;

                var inv = await MirrorAsync(sub, ev, prices, start, end);
                BillingService.SetState(sub, SubscriptionRules.ApplyPayment(BillingService.State(sub), start, end));
                Link(sub, ev);
                SetItems(sub, ev.Items, prices);
                sub.NextModules = null;
                sub.NextBranchQuantity = null;
                sub.NextInterval = null;
                // Paying by card replaces any bank transfer invoice still open.
                foreach (var open in await _db.Invoices.Where(i => i.SubscriptionId == sub.Id && i.Status == InvoiceStatus.Open && i.Provider == BillingProvider.BankTransfer).ToListAsync())
                {
                    open.Status = InvoiceStatus.Void;
                    open.VoidedUtc = utcNow;
                }
                Audit(sub, $"paid by card {inv.Number}", before, utcNow);
                return new ApplyResult(inv, sub.OwnerId);
            }

            case "transaction.payment_failed":
                Audit(sub, $"card payment failed ({ev.ObjectId})", before, utcNow);
                return ApplyResult.None; // the status follows subscription.past_due

            default: // subscription.*
            {
                if (sub.ProviderSyncedUtc is { } last && ev.OccurredUtc <= last) return ApplyResult.None; // older or replayed
                Link(sub, ev);
                sub.ProviderSyncedUtc = ev.OccurredUtc;
                sub.CancelAtPeriodEnd = ev.CancelScheduled;
                if (ev.PeriodStartUtc is { } ps && ev.PeriodEndUtc is { } pe)
                {
                    sub.CurrentPeriodStartUtc = ps;
                    sub.CurrentPeriodEndUtc = pe;
                }
                SetItems(sub, ev.Items, prices);

                string? email = null;
                var was = sub.Status;
                switch (ev.Status)
                {
                    case "active" or "trialing":
                        sub.Status = SubscriptionStatus.Active;
                        sub.IsLegacy = false;
                        sub.GraceEndsUtc = null;
                        break;
                    case "past_due":
                        sub.Status = SubscriptionStatus.PastDue;
                        sub.GraceEndsUtc ??= utcNow.AddDays(settings.GraceDays);
                        if (was != SubscriptionStatus.PastDue) email = "past-due";
                        break;
                    case "paused":
                        sub.Status = SubscriptionStatus.ReadOnly;
                        if (was != SubscriptionStatus.ReadOnly) email = "read-only";
                        break;
                    case "canceled":
                        sub.Status = SubscriptionStatus.Cancelled;
                        sub.CancelAtPeriodEnd = false;
                        if (was != SubscriptionStatus.Cancelled) email = "cancelled";
                        break;
                }
                sub.UpdatedUtc = utcNow;
                Audit(sub, $"paddle {ev.EventType} ({ev.Status})", before, utcNow);
                return new ApplyResult(null, sub.OwnerId, email);
            }
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(PaddleEvent ev)
    {
        var q = _db.Subscriptions.IgnoreQueryFilters().Include(s => s.Items).Include(s => s.Owner);
        if (ev.OwnerId != null && await q.FirstOrDefaultAsync(s => s.OwnerId == ev.OwnerId) is { } byOwner) return byOwner;
        if (ev.SubscriptionId != null && await q.FirstOrDefaultAsync(s => s.ProviderSubscriptionId == ev.SubscriptionId) is { } bySub) return bySub;
        if (ev.CustomerId != null) return await q.FirstOrDefaultAsync(s => s.ProviderCustomerId == ev.CustomerId);
        return null;
    }

    private void Link(Subscription sub, PaddleEvent ev)
    {
        sub.Provider = BillingProvider.Paddle;
        // Webhooks are signed with this account's secret, so their ids are this account's.
        sub.ProviderEnvironment = Env;
        sub.ProviderCustomerId = ev.CustomerId ?? sub.ProviderCustomerId;
        sub.ProviderSubscriptionId = ev.SubscriptionId ?? sub.ProviderSubscriptionId;
    }

    /// <summary>The modules and branch count the provider is charging for become the account's.</summary>
    private void SetItems(Subscription sub, IReadOnlyList<PaddleItem> items, Dictionary<string, (BillingModule Module, BillingInterval Interval, int Cents)> prices)
    {
        var known = items.Where(i => prices.ContainsKey(i.PriceId)).Select(i => (Item: i, Price: prices[i.PriceId])).ToList();
        if (known.Count == 0) return;
        var modules = known.Select(k => k.Price.Module).ToHashSet();
        foreach (var gone in sub.Items.Where(i => !modules.Contains(i.Module)).ToList()) sub.Items.Remove(gone);
        foreach (var (item, price) in known)
        {
            var row = sub.Items.FirstOrDefault(i => i.Module == price.Module);
            if (row == null) sub.Items.Add(row = new SubscriptionItem { Module = price.Module });
            row.Quantity = item.Quantity;
            row.UnitAmountCents = price.Cents;
        }
        sub.Interval = known[0].Price.Interval;
        var branches = known.Where(k => EntitlementRules.IsPerBranch(k.Price.Module)).Select(k => k.Item.Quantity).DefaultIfEmpty(sub.BranchQuantity).Max();
        sub.BranchQuantity = Math.Max(1, branches);
        if (sub.Owner != null) sub.Owner.NumberOfBranches = sub.BranchQuantity;
    }

    /// <summary>A Paddle charge as an invoice in the account's history (Paddle's receipt is the official one).</summary>
    private async Task<Invoice> MirrorAsync(Subscription sub, PaddleEvent ev, Dictionary<string, (BillingModule Module, BillingInterval Interval, int Cents)> prices,
        DateTime start, DateTime end)
    {
        var owner = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == sub.OwnerId);
        sub.Owner = owner;
        var profile = await _db.BillingProfiles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.OwnerId == sub.OwnerId);
        var number = ev.InvoiceNumber is { Length: > 0 and <= 20 } n ? n : "PDL-" + ev.ObjectId[^Math.Min(14, ev.ObjectId.Length)..];
        var known = ev.Items.Where(i => prices.ContainsKey(i.PriceId)).ToList();
        var subtotal = ev.SubtotalCents ?? known.Sum(i => prices[i.PriceId].Cents * i.Quantity);
        var tax = ev.TaxCents ?? 0;
        var inv = new Invoice
        {
            Number = number, Year = start.Year, Sequence = 0,
            OwnerId = sub.OwnerId, SubscriptionId = sub.Id,
            PeriodStartUtc = start, PeriodEndUtc = end,
            Interval = known.Count > 0 ? prices[known[0].PriceId].Interval : sub.Interval,
            BranchQuantity = known.Where(i => EntitlementRules.IsPerBranch(prices[i.PriceId].Module)).Select(i => i.Quantity).DefaultIfEmpty(sub.BranchQuantity).Max(),
            Kind = ev.Origin == "subscription_recurring" ? "renewal" : ev.Origin == "subscription_update" ? "change" : "first",
            Currency = ev.Currency ?? sub.Currency,
            SubtotalCents = subtotal, VatCents = tax, TotalCents = ev.TotalCents ?? subtotal + tax,
            VatPercent = subtotal > 0 ? Math.Round(tax * 100m / subtotal, 2) : 0,
            Status = InvoiceStatus.Paid, IssuedUtc = ev.OccurredUtc, DueUtc = ev.OccurredUtc, PaidUtc = ev.OccurredUtc, PaidNote = "Card (Paddle)",
            Provider = BillingProvider.Paddle, ProviderRef = ev.ObjectId, Language = owner.Language,
            SellerName = "Paddle.com Market Ltd (reseller)",
            BuyerName = profile?.LegalName ?? owner.FullName, BuyerNipt = profile?.Nipt, BuyerEmail = profile?.BillingEmail ?? owner.Email!
        };
        foreach (var i in known)
        {
            var p = prices[i.PriceId];
            inv.Lines.Add(new InvoiceLine { Module = p.Module, Quantity = i.Quantity, UnitCents = p.Cents, TotalCents = p.Cents * i.Quantity });
        }
        _db.Invoices.Add(inv);
        return inv;
    }

    private void Audit(Subscription sub, string action, string before, DateTime utcNow) =>
        _db.SubscriptionAudits.Add(new SubscriptionAudit
        {
            SubscriptionId = sub.Id, Action = action, FromJson = before, ToJson = SubscriptionService.Describe(sub), AtUtc = utcNow
        });
}
