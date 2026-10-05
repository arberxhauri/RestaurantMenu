using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

// Owners see their branches; staff see the branches they were invited to.
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MenuInsights _insights;
    private readonly IBranchAccess _access;
    private readonly IEntitlementService _entitlements;
    private readonly SiteHosts _hosts;

    public DashboardController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        MenuInsights insights,
        IBranchAccess access,
        IEntitlementService entitlements,
        SiteHosts hosts)
    {
        _entitlements = entitlements;
        _hosts = hosts;
        _context = context;
        _userManager = userManager;
        _insights = insights;
        _access = access;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var branches = await _access.Branches(BranchPermission.View)
            .Where(b => !b.IsDeleted)
            .Include(b => b.Categories).ThenInclude(c => c.Products)
            .OrderBy(b => b.Name)
            .ToListAsync();

        // The user's role on each branch: owner, or their team role.
        var memberRoles = await _context.BranchMembers
            .Where(m => m.UserId == user.Id)
            .ToDictionaryAsync(m => m.BranchId, m => m.Role);
        var roles = branches.ToDictionary(b => b.Id, b =>
            b.UserId == user.Id ? BranchRole.Owner
            : memberRoles.GetValueOrDefault(b.Id) == BranchMemberRole.Manager ? BranchRole.Manager
            : BranchRole.Editor);

        // Quota and "New branch" are about the branches this user owns.
        var isOwner = User.IsInRole("OWNER");
        var owned = branches.Count(b => b.UserId == user.Id);
        ViewBag.Roles = roles;
        ViewBag.IsOwner = isOwner;
        ViewBag.OwnedCount = owned;
        // The plan decides the branch count; branches past it are paused (any owner's, for staff too).
        var plan = isOwner ? await _entitlements.ForOwnerAsync(user.Id) : null;
        ViewBag.CanCreateBranch = plan != null && plan.CanWrite && owned < plan.MaxBranches;
        ViewBag.MaxBranches = plan?.MaxBranches ?? 0;
        ViewBag.OverLimit = plan != null && plan.CanWrite && owned > plan.MaxBranches;
        var paused = new HashSet<int>();
        foreach (var ownerId in branches.Select(b => b.UserId).Distinct())
            paused.UnionWith(await _entitlements.PausedBranchesAsync(ownerId));
        ViewBag.Paused = paused;

        // Onboarding for self-serve owners until they hide it: branch, dishes, QR code.
        if (isOwner && user.SignupSource == SignupSource.SelfServe && user.OnboardingDoneUtc == null)
        {
            var own = branches.Where(b => b.UserId == user.Id).OrderBy(b => b.Id).ToList();
            ViewBag.Onboarding = new OnboardingModel(
                Helpers.SignupText.For(user.Language),
                FirstBranchId: own.FirstOrDefault()?.Id,
                HasBranch: own.Count > 0,
                HasDishes: own.Any(b => b.Categories?.Any(c => c.Products?.Any() == true) == true),
                PrintedQr: user.OnboardingQrUtc != null);
        }
        ViewBag.Week = await _insights.WeekByBranchAsync(
            branches.Where(b => BranchAccess.Allows(roles[b.Id], BranchPermission.ViewInsights)).Select(b => b.Id).ToList());
        return View(branches);
    }

    /// <summary>Hides the onboarding checklist for good.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "OWNER")]
    public async Task<IActionResult> HideOnboarding()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null && user.OnboardingDoneUtc == null)
        {
            user.OnboardingDoneUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Over the plan's branch count: the owner picks which branches stay active (KeepActive);
    /// the others pause until the plan has room. At most the plan's count may be picked.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "OWNER")]
    public async Task<IActionResult> ChooseActive(int[]? keep)
    {
        var userId = _userManager.GetUserId(User)!;
        var plan = await _entitlements.ForOwnerAsync(userId);
        var picked = (keep ?? Array.Empty<int>()).Distinct().ToHashSet();
        if (picked.Count == 0 || picked.Count > plan.MaxBranches)
        {
            TempData["Error"] = $"Pick between 1 and {plan.MaxBranches} branch{(plan.MaxBranches == 1 ? "" : "es")} to keep active.";
            return RedirectToAction(nameof(Index));
        }

        var own = await _context.Branches.Where(b => b.UserId == userId).ToListAsync();
        if (!picked.IsSubsetOf(own.Select(b => b.Id))) return NotFound();
        foreach (var b in own) b.KeepActive = picked.Contains(b.Id);
        await _context.SaveChangesAsync();
        _hosts.Invalidate();

        TempData["Success"] = $"Saved. {string.Join(", ", own.Where(b => picked.Contains(b.Id)).Select(b => b.Name))} stay{(picked.Count == 1 ? "s" : "")} active; the rest are paused, with their menus still online.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>The dashboard's "get your menu live" checklist (self-serve owners, in their language).</summary>
public record OnboardingModel(Helpers.SignupText.Words W, int? FirstBranchId, bool HasBranch, bool HasDishes, bool PrintedQr)
{
    public bool AllDone => HasBranch && HasDishes && PrintedQr;
}
