using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER")]
[NoIndex]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MenuInsights _insights;

    public DashboardController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        MenuInsights insights)
    {
        _context = context;
        _userManager = userManager;
        _insights = insights;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var branches = await _context.Branches
            .Where(b => b.UserId == user.Id && !b.IsDeleted)
            .Include(b => b.Categories).ThenInclude(c => c.Products)
            .ToListAsync();

        ViewBag.CanCreateBranch = branches.Count < user.NumberOfBranches;
        ViewBag.MaxBranches = user.NumberOfBranches;
        ViewBag.Week = await _insights.WeekByBranchAsync(branches.Select(b => b.Id).ToList());
        return View(branches);
    }
}