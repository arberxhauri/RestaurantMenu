using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>The kinds of plan the admin can put an owner on before payments exist.</summary>
public enum PlanKind { Legacy, Trial, Active, ReadOnly }

/// <summary>An admin's change to a subscription (Admin → Plan).</summary>
public record SubscriptionChange(
    PlanKind Kind,
    IReadOnlySet<BillingModule> Modules,
    int Branches,
    int? SeatsPerBranch,
    int OwnDomains,
    BillingInterval Interval,
    DateTime? TrialEndsUtc,
    DateTime? PeriodEndUtc);

/// <summary>
/// Writes subscriptions: creating them, applying changes with an audit row, keeping the old
/// branch allowance (ApplicationUser.NumberOfBranches) in step until it is retired in phase 6.
/// Reading what an account may do is IEntitlementService's job.
/// </summary>
public class SubscriptionService
{
    private readonly ApplicationDbContext _db;
    private readonly BillingOptions _options;

    public SubscriptionService(ApplicationDbContext db, IOptions<BillingOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public Task<Subscription?> FindAsync(string ownerId) =>
        _db.Subscriptions.Include(s => s.Items).FirstOrDefaultAsync(s => s.OwnerId == ownerId);

    /// <summary>
    /// A legacy subscription: every module, at least as many branches as the owner has now,
    /// no charge. Added to the context, not saved.
    /// </summary>
    public static Subscription NewLegacy(string ownerId, int branches, string currency, DateTime utcNow)
    {
        var s = new Subscription
        {
            OwnerId = ownerId,
            Status = SubscriptionStatus.Active,
            IsLegacy = true,
            Interval = BillingInterval.Month,
            Currency = currency,
            BranchQuantity = Math.Max(branches, 1),
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow
        };
        foreach (var m in EntitlementRules.AllModules)
            s.Items.Add(new SubscriptionItem { Module = m, Quantity = EntitlementRules.IsPerBranch(m) ? s.BranchQuantity : 1, UnitAmountCents = 0 });
        return s;
    }

    /// <summary>For a new owner made by the admin: legacy terms, as owners always had until plans are priced.</summary>
    public void AddLegacy(ApplicationUser owner, DateTime utcNow)
    {
        var s = NewLegacy(owner.Id, owner.NumberOfBranches, _options.Currency, utcNow);
        _db.Subscriptions.Add(s);
    }

    /// <summary>
    /// Applies an admin's change and records it. <paramref name="expectedVersion"/> is the version
    /// the form was built from. Null when saved; otherwise what went wrong (someone else changed
    /// the plan since the form was opened).
    /// </summary>
    public async Task<string?> ApplyAsync(string ownerId, SubscriptionChange change, uint? expectedVersion, string? actorId, DateTime utcNow)
    {
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        if (owner == null) return "That owner no longer exists.";
        var s = await FindAsync(ownerId);
        var created = s == null;
        if (s == null)
        {
            var live = await _db.Branches.CountAsync(b => b.UserId == ownerId);
            s = NewLegacy(ownerId, Math.Max(owner.NumberOfBranches, live), _options.Currency, utcNow);
            _db.Subscriptions.Add(s);
        }
        var before = created ? null : Describe(s);
        if (!created && expectedVersion is { } v) _db.Entry(s).Property(x => x.Version).OriginalValue = v;

        s.IsLegacy = change.Kind == PlanKind.Legacy;
        s.Status = change.Kind switch
        {
            PlanKind.Trial => SubscriptionStatus.Trialing,
            PlanKind.ReadOnly => SubscriptionStatus.ReadOnly,
            _ => SubscriptionStatus.Active
        };
        s.Interval = change.Interval;
        s.BranchQuantity = Math.Clamp(change.Branches, 1, 100);
        s.SeatsPerBranch = change.Kind == PlanKind.Legacy ? null : change.SeatsPerBranch is { } seats ? Math.Clamp(seats, 0, 100) : null;
        s.TrialEndsUtc = change.Kind == PlanKind.Trial ? change.TrialEndsUtc ?? utcNow.AddDays(_options.TrialDays) : null;
        s.CurrentPeriodStartUtc = change.Kind == PlanKind.Active ? s.CurrentPeriodStartUtc ?? utcNow : null;
        s.CurrentPeriodEndUtc = change.Kind == PlanKind.Active ? change.PeriodEndUtc : null;
        s.GraceEndsUtc = null;
        s.CancelAtPeriodEnd = false;
        s.UpdatedUtc = utcNow;

        // Modules: legacy has them all; otherwise what was ticked, plus the menu, minus any whose base is off.
        var modules = change.Kind == PlanKind.Legacy
            ? EntitlementRules.AllModules.ToHashSet()
            : change.Modules.Append(BillingModule.Menu).ToHashSet();
        modules.RemoveWhere(m => EntitlementRules.Requires(m) is { } needed && !modules.Contains(needed));
        if (change.Kind != PlanKind.Legacy && change.OwnDomains <= 0) modules.Remove(BillingModule.OwnDomain);

        var prices = await CurrentPricesAsync(s.Currency, s.Interval, utcNow);
        foreach (var gone in s.Items.Where(i => !modules.Contains(i.Module)).ToList()) s.Items.Remove(gone);
        foreach (var m in modules)
        {
            var item = s.Items.FirstOrDefault(i => i.Module == m);
            if (item == null) s.Items.Add(item = new SubscriptionItem { Module = m });
            item.Quantity = m switch
            {
                BillingModule.OwnDomain => change.Kind == PlanKind.Legacy ? 1 : Math.Clamp(change.OwnDomains, 1, 100),
                BillingModule.Sms => 1,
                _ => s.BranchQuantity
            };
            // Prices are recorded for paid plans only; legacy and trials are free.
            item.UnitAmountCents = change.Kind == PlanKind.Active ? prices.GetValueOrDefault(m) : 0;
        }

        // The old allowance follows the plan, so any code still reading it agrees.
        owner.NumberOfBranches = s.BranchQuantity;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return "Someone else changed this plan at the same time. Reload the page and try again.";
        }

        _db.SubscriptionAudits.Add(new SubscriptionAudit
        {
            SubscriptionId = s.Id,
            ActorId = actorId,
            Action = created ? "created" : "changed",
            FromJson = before,
            ToJson = Describe(s),
            AtUtc = utcNow
        });
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>The price per unit that applies now for each module, in cents.</summary>
    public async Task<Dictionary<BillingModule, int>> CurrentPricesAsync(string currency, BillingInterval interval, DateTime utcNow)
    {
        var rows = await _db.PriceBook.AsNoTracking()
            .Where(p => p.Currency == currency && p.Interval == interval && p.ValidFromUtc <= utcNow)
            .ToListAsync();
        return rows.GroupBy(p => p.Module)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.ValidFromUtc).First().UnitAmountCents);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>A subscription as JSON for the audit log: what someone reading the history needs.</summary>
    public static string Describe(Subscription s) => JsonSerializer.Serialize(new
    {
        status = s.Status.ToString(),
        legacy = s.IsLegacy,
        interval = s.Interval.ToString(),
        branches = s.BranchQuantity,
        seats = s.SeatsPerBranch,
        trialEnds = s.TrialEndsUtc,
        periodEnds = s.CurrentPeriodEndUtc,
        modules = s.Items.OrderBy(i => i.Module).Select(i => new { module = i.Module.ToString(), qty = i.Quantity, cents = i.UnitAmountCents })
    }, Json);

    // Any fixed number; only the startup setup takes it.
    private const long SetupLockKey = 0x4D514D_42494C4C; // "MQM BILL"

    /// <summary>
    /// Startup, after migrations: copies configured prices into the price book when they changed,
    /// and gives every owner without a subscription a legacy one (every module, their branch
    /// allowance or their live branch count if higher, no charge), so nobody loses anything the
    /// day plans arrive. Logged; a no-op once done; safe with several instances.
    /// </summary>
    public static async Task SetupAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<BillingOptions>>().Value;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTime.UtcNow;

        var owners = (await users.GetUsersInRoleAsync("OWNER")).Where(u => !u.IsDeleted).Select(u => u.Id).ToList();

        var strategy = db.Database.CreateExecutionStrategy();
        var log = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var lines = new List<string>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})");

            // Prices from configuration.
            var book = await db.PriceBook.ToListAsync();
            foreach (var (moduleName, intervals) in options.Prices)
            {
                if (!Enum.TryParse<BillingModule>(moduleName, true, out var module))
                {
                    lines.Add($"Billing: unknown module \"{moduleName}\" in Billing__Prices, ignored");
                    continue;
                }
                foreach (var (intervalName, cents) in intervals)
                {
                    if (!Enum.TryParse<BillingInterval>(intervalName, true, out var interval) || cents < 0)
                    {
                        lines.Add($"Billing: price {moduleName}/{intervalName} = {cents} ignored (interval is Month or Year, amount in cents ≥ 0)");
                        continue;
                    }
                    var current = book.Where(p => p.Module == module && p.Interval == interval && p.Currency == options.Currency)
                        .OrderByDescending(p => p.ValidFromUtc).FirstOrDefault();
                    if (current?.UnitAmountCents == cents) continue;
                    db.PriceBook.Add(new PriceBook { Module = module, Interval = interval, Currency = options.Currency, UnitAmountCents = cents, ValidFromUtc = now });
                    lines.Add(FormattableString.Invariant($"Billing: price {module}/{interval} is now {cents / 100m:0.00} {options.Currency}") +
                              (current == null ? "" : FormattableString.Invariant($" (was {current.UnitAmountCents / 100m:0.00})")));
                }
            }

            // Legacy subscriptions for owners without one.
            var have = (await db.Subscriptions.IgnoreQueryFilters().Select(s => s.OwnerId).ToListAsync()).ToHashSet();
            var missing = owners.Where(id => !have.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                var allowance = await db.Users.Where(u => missing.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NumberOfBranches);
                var live = await db.Branches.Where(b => missing.Contains(b.UserId)).GroupBy(b => b.UserId)
                    .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                foreach (var id in missing)
                {
                    var branches = Math.Max(allowance.GetValueOrDefault(id), live.GetValueOrDefault(id));
                    db.Subscriptions.Add(NewLegacy(id, branches, options.Currency, now));
                }
                await db.SaveChangesAsync();
                var created = await db.Subscriptions.Include(s => s.Items).Where(s => missing.Contains(s.OwnerId)).ToListAsync();
                foreach (var s in created)
                    db.SubscriptionAudits.Add(new SubscriptionAudit { SubscriptionId = s.Id, Action = "legacy (startup)", ToJson = Describe(s), AtUtc = now });
                lines.Add($"Billing: {missing.Count} existing owner(s) moved to a legacy subscription (every module, no charge)");
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return lines;
        });

        foreach (var line in log)
        {
#pragma warning disable CA2254 // built above from values, not user input
            logger.LogInformation(line);
#pragma warning restore CA2254
        }
    }
}
