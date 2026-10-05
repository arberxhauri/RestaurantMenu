using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>A branch found by a link: its id and the slug it should be reached at.</summary>
public record SlugMatch(int BranchId, string Slug);

/// <summary>
/// Branch links in the database: finding a branch by its slug or an old link, giving a new
/// branch a free slug, keeping the old link when a rename changes it, and filling slugs for
/// branches made before they were stored. The rules themselves are in SlugRules.
/// </summary>
public class BranchSlugs
{
    private readonly ApplicationDbContext _db;

    public BranchSlugs(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// The live branch a link segment leads to: by its slug, else by one of its old links.
    /// Case-insensitive; null for unknown links and deleted branches. Callers redirect when
    /// the segment isn't exactly <see cref="SlugMatch.Slug"/>.
    /// </summary>
    public async Task<SlugMatch?> ResolveAsync(string? routeValue)
    {
        var key = SlugRules.Key(routeValue);
        if (key.Length == 0 || key.Length > 200) return null;
        return await _db.Branches.AsNoTracking()
                   .Where(b => b.Slug == key)
                   .Select(b => new SlugMatch(b.Id, b.Slug))
                   .FirstOrDefaultAsync()
               ?? await _db.BranchSlugAliases.AsNoTracking()
                   .Where(a => a.Slug == key)
                   .Select(a => new SlugMatch(a.BranchId, a.Branch!.Slug))
                   .FirstOrDefaultAsync();
    }

    /// <summary>
    /// The link a new branch with this name would get right now, and whether its plain link is
    /// already taken (signup shows "yours will be oliva-2"). Nothing is reserved.
    /// </summary>
    public async Task<(string Slug, bool Taken)> SuggestAsync(string? name)
    {
        var wanted = SlugRules.FromName(name);
        var slug = SlugRules.Unique(name, await TakenAsync(wanted, 0));
        return (slug, slug != wanted);
    }

    /// <summary>Gives a new branch the first free slug for its name.</summary>
    public async Task AssignAsync(Branch branch)
    {
        branch.Slug = SlugRules.Unique(branch.Name, await TakenAsync(SlugRules.FromName(branch.Name), branch.Id));
    }

    /// <summary>
    /// After a rename: a new slug when the name asks for a different one, with the old slug
    /// kept as an old link. Cosmetic changes ("Oliva" → "OLIVA") keep the link as it is. Taking
    /// back one of the branch's own old links ("rename it back") is allowed.
    /// Returns the old slug when the link changed, else null.
    /// </summary>
    public async Task<string?> RenameAsync(Branch branch, string oldName)
    {
        var wanted = SlugRules.FromName(branch.Name);
        if (wanted == SlugRules.FromName(oldName) && !string.IsNullOrEmpty(branch.Slug)) return null;

        var slug = SlugRules.Unique(branch.Name, await TakenAsync(wanted, branch.Id));
        var old = branch.Slug;
        if (slug == old) return null;

        var reclaimed = await _db.BranchSlugAliases.Where(a => a.BranchId == branch.Id && a.Slug == slug).ToListAsync();
        _db.BranchSlugAliases.RemoveRange(reclaimed);
        if (!string.IsNullOrEmpty(old) && !await _db.BranchSlugAliases.IgnoreQueryFilters().AnyAsync(a => a.Slug == old))
        {
            _db.BranchSlugAliases.Add(new BranchSlugAlias { BranchId = branch.Id, Slug = old, CreatedUtc = DateTime.UtcNow });
        }
        branch.Slug = slug;
        return string.IsNullOrEmpty(old) ? null : old;
    }

    /// <summary>
    /// Saves, and if another branch took the same slug in the meantime (the unique index
    /// says so), picks the next free one and saves again.
    /// </summary>
    public async Task SaveAsync(Branch branch)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException e) when (attempt < 4 && IsSlugClash(e))
            {
                branch.Slug = SlugRules.Unique(branch.Name, await TakenAsync(SlugRules.FromName(branch.Name), branch.Id));
            }
        }
    }

    private static bool IsSlugClash(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName ?? "").Contains("Slug", StringComparison.Ordinal);

    /// <summary>
    /// Slugs and old links used by other branches (deleted ones too) that could clash with
    /// candidates for <paramref name="baseSlug"/>: they all start with its first 40 characters.
    /// </summary>
    private async Task<HashSet<string>> TakenAsync(string baseSlug, int branchId)
    {
        var prefix = baseSlug.Length > 40 ? baseSlug[..40] : baseSlug;
        var slugs = await _db.Branches.IgnoreQueryFilters()
            .Where(b => b.Id != branchId && b.Slug != null && b.Slug.StartsWith(prefix))
            .Select(b => b.Slug).ToListAsync();
        var aliases = await _db.BranchSlugAliases.IgnoreQueryFilters()
            .Where(a => a.BranchId != branchId && a.Slug.StartsWith(prefix))
            .Select(a => a.Slug).ToListAsync();
        return new HashSet<string>(slugs.Concat(aliases), StringComparer.Ordinal);
    }

    // Any fixed number; only the backfill takes it.
    private const long BackfillLockKey = 0x4D514D_534C5547; // "MQM SLUG"

    /// <summary>
    /// Gives every branch without a slug one (live branches first, then oldest first, so the
    /// restaurant that had a link longest keeps the plain one), and keeps its old name-based
    /// link as an old link so printed QR codes still open. Logs each one; a warning when a
    /// clash meant a suffix or an old link that two branches shared. Runs at startup, does
    /// nothing once every branch has a slug, and is safe with several instances (advisory lock).
    /// </summary>
    public static async Task BackfillAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Branches.IgnoreQueryFilters().AnyAsync(b => b.Slug == null)) return;

        // One transaction under the retrying strategy; the log lines wait for the commit so a
        // retried attempt doesn't log twice.
        var strategy = db.Database.CreateExecutionStrategy();
        var lines = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var log = new List<(LogLevel Level, string Message, object?[] Args)>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({BackfillLockKey})");

            var branches = await db.Branches.IgnoreQueryFilters()
                .OrderBy(b => b.IsDeleted).ThenBy(b => b.Id)
                .ToListAsync();
            var aliases = await db.BranchSlugAliases.IgnoreQueryFilters().ToListAsync();
            var missing = branches.Where(b => b.Slug == null).ToList();
            if (missing.Count == 0) return log; // another instance got here first

            var taken = new HashSet<string>(branches.Where(b => b.Slug != null).Select(b => b.Slug)
                .Concat(aliases.Select(a => a.Slug)), StringComparer.Ordinal);
            var ownerOfLegacy = new Dictionary<string, Branch>(StringComparer.Ordinal);

            // Slugs first, so a branch's new slug always beats another branch's old link.
            foreach (var b in missing)
            {
                b.Slug = SlugRules.Unique(b.Name, taken);
                taken.Add(b.Slug);
                log.Add(b.Slug != SlugRules.FromName(b.Name)
                    ? (LogLevel.Warning, "Branch links: branch {Id} \"{Name}\" gets /menu/{Slug} (its plain link was taken)", new object?[] { b.Id, b.Name, b.Slug })
                    : (LogLevel.Information, "Branch links: branch {Id} \"{Name}\" gets /menu/{Slug}", new object?[] { b.Id, b.Name, b.Slug }));
            }

            foreach (var b in missing)
            {
                var legacy = SlugRules.Legacy(b.Name ?? "");
                if (legacy.Length == 0 || legacy.Length > 200 || legacy == b.Slug) continue;
                if (ownerOfLegacy.TryGetValue(legacy, out var first))
                {
                    log.Add((LogLevel.Warning, "Branch links: old link /menu/{Legacy} was shared by branches {First} and {Id}; it now opens branch {First} only",
                        new object?[] { legacy, first.Id, b.Id, first.Id }));
                    continue;
                }
                if (taken.Contains(legacy))
                {
                    log.Add((LogLevel.Warning, "Branch links: old link /menu/{Legacy} of branch {Id} is now another branch's link", new object?[] { legacy, b.Id }));
                    continue;
                }
                db.BranchSlugAliases.Add(new BranchSlugAlias { BranchId = b.Id, Slug = legacy, CreatedUtc = DateTime.UtcNow });
                taken.Add(legacy);
                ownerOfLegacy[legacy] = b;
                log.Add((LogLevel.Information, "Branch links: old link /menu/{Legacy} now leads to /menu/{Slug}", new object?[] { legacy, b.Slug }));
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            log.Add((LogLevel.Information, "Branch links: gave {Count} branch(es) a link", new object?[] { missing.Count }));
            return log;
        });

        foreach (var (level, message, args) in lines)
        {
#pragma warning disable CA2254 // the templates above are constant
            logger.Log(level, message, args);
#pragma warning restore CA2254
        }
    }
}
