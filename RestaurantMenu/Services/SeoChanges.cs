using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Primitives;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// "Something a search engine sees has changed": branches whose menu, website, hours or links were
/// saved. The sitemaps drop their cache (<see cref="Token"/>) and IndexNow pings the branch's
/// pages (<see cref="BranchesChanged"/>). Raised by <see cref="SeoChangeInterceptor"/> after every
/// save, so no controller has to remember to call it.
/// </summary>
public class SeoChanges
{
    private CancellationTokenSource _cts = new();

    /// <summary>Fires on the next change; caches built from the database expire with it.</summary>
    public IChangeToken Token => new CancellationChangeToken(_cts.Token);

    /// <summary>Branch ids after each save that touched them. Handlers must be quick and must not throw.</summary>
    public event Action<IReadOnlyCollection<int>>? BranchesChanged;

    public void Notify(IReadOnlyCollection<int> branchIds)
    {
        if (branchIds.Count == 0) return;
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
        BranchesChanged?.Invoke(branchIds);
    }

    /// <summary>For marketing pages and anything not tied to a branch (a deploy, a price change).</summary>
    public void NotifyAll()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}

/// <summary>
/// Stamps UpdatedUtc on branches, categories and dishes (the menu's Last-Modified and the
/// sitemap's lastmod), then tells <see cref="SeoChanges"/> which branches changed. Registered
/// after SoftDeleteInterceptor, so a deleted dish counts as a change to its menu.
/// </summary>
public class SeoChangeInterceptor : SaveChangesInterceptor
{
    private readonly SeoChanges _changes;
    // Entities seen while saving, read again once saved (a new branch only has its id then).
    private readonly ConditionalWeakTable<DbContext, List<object>> _pending = new();

    public SeoChangeInterceptor(SeoChanges changes) => _changes = changes;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        if (context == null) return;
        var now = DateTime.UtcNow;
        var seen = new List<object>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (entry.Entity)
            {
                case Branch b:
                    if (entry.State != EntityState.Deleted && !OnlyCounters(entry)) { b.UpdatedUtc = now; seen.Add(b); }
                    break;
                case Category c:
                    if (entry.State != EntityState.Deleted) c.UpdatedUtc = now;
                    seen.Add(c);
                    break;
                case Product p:
                    if (entry.State != EntityState.Deleted) p.UpdatedUtc = now;
                    seen.Add(p);
                    break;
                case BranchSite or Domain or BranchHours or BranchSlugAlias or ReservationSettings:
                    seen.Add(entry.Entity);
                    break;
            }
        }
        if (seen.Count > 0) _pending.AddOrUpdate(context, seen);
    }

    /// <summary>
    /// Saves that only move order counters or the kitchen's pause switch happen during every
    /// service; they change nothing a crawler sees.
    /// </summary>
    private static bool OnlyCounters(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        entry.State == EntityState.Modified && entry.Properties.Where(p => p.IsModified).All(p =>
            p.Metadata.Name is nameof(Branch.OrderCounter) or nameof(Branch.OrderCounterDay) or nameof(Branch.OrdersPaused) or nameof(Branch.KeepActive));

    private void Flush(DbContext? context)
    {
        if (context == null || !_pending.TryGetValue(context, out var seen)) return;
        _pending.Remove(context);
        var ids = new HashSet<int>();
        foreach (var e in seen)
        {
            ids.Add(e switch
            {
                Branch b => b.Id,
                Category c => c.BranchId,
                Product p => p.BranchId,
                BranchSite s => s.BranchId,
                Domain d => d.BranchId,
                BranchHours h => h.BranchId,
                BranchSlugAlias a => a.BranchId,
                ReservationSettings r => r.BranchId,
                _ => 0
            });
        }
        ids.Remove(0);
        _changes.Notify(ids);
    }
}
