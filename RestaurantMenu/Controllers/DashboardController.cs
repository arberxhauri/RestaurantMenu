using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER")]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
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
        return View(branches);
    }
}