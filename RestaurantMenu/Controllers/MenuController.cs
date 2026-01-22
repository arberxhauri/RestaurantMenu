using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Controllers;

public class MenuController : Controller
{
    private readonly ApplicationDbContext _context;

    public MenuController(ApplicationDbContext context)
    {
        _context = context;
    }

    [Route("menu/{branchName}")]
    public async Task<IActionResult> Index(string branchName, string lang = "en")
    {
        branchName = branchName.Trim();

        var decodedName = Uri.UnescapeDataString(branchName);

        var branch = await _context.Branches
            .Include(b => b.Categories.OrderBy(c => c.Priority))
            .ThenInclude(c => c.Products.OrderBy(p => p.DisplayOrder))
            .FirstOrDefaultAsync(b => b.Name.Replace(" ", "").ToLower() == decodedName.ToLower() && !b.IsDeleted);

        if (branch == null)
        {
            return NotFound();
        }

        // Validate language
        var supportedLanguages = branch.SupportedLanguages.Split(',');
        if (!supportedLanguages.Contains(lang))
        {
            lang = "en"; // Fallback to English
        }

        ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.CurrentLanguage = lang;
        ViewBag.SupportedLanguages = supportedLanguages;
        ViewBag.ThemeColors = branch.ThemeColors;
            
        return View(branch);
    }
}
