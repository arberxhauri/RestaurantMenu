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

    public DashboardController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        MenuInsights insights,
        IBranchAccess access)
    {
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
        ViewBag.CanCreateBranch = isOwner && owned < user.NumberOfBranches;
        ViewBag.MaxBranches = user.NumberOfBranches;
        ViewBag.Week = await _insights.WeekByBranchAsync(
            branches.Where(b => BranchAccess.Allows(roles[b.Id], BranchPermission.ViewInsights)).Select(b => b.Id).ToList());
        return View(branches);
    }
}