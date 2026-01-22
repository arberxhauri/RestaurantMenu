using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER")]
    public class BranchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ColorExtractionService _colorService;
        
        public BranchController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            ColorExtractionService colorService)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _colorService = colorService;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            var branchCount = await _context.Branches
                .CountAsync(b => b.UserId == user.Id && !b.IsDeleted);

            if (branchCount >= user.NumberOfBranches)
            {
                TempData["Error"] = "You have reached your branch limit.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Currencies = CurrencyHelper.GetCurrencies();
            return View();
        }

        [HttpPost]
public async Task<IActionResult> Create(Branch branch, IFormFile? logo, IFormFile? banner, string[] selectedLanguages)
{
    var user = await _userManager.GetUserAsync(User);
    
    var branchCount = await _context.Branches
        .CountAsync(b => b.UserId == user.Id && !b.IsDeleted);

    if (branchCount >= user.NumberOfBranches)
    {
        TempData["Error"] = "You have reached your branch limit.";
        return RedirectToAction("Index", "Dashboard");
    }

    var nameExists = await _context.Branches
        .AnyAsync(b => b.Name.ToLower() == branch.Name.ToLower() && !b.IsDeleted);

    if (nameExists)
    {
        ModelState.AddModelError("Name", "This branch name is already taken.");
    }

    branch.UserId = user.Id;
    
    // Set supported languages
    if (selectedLanguages != null && selectedLanguages.Length > 0)
    {
        branch.SupportedLanguages = string.Join(",", selectedLanguages);
    }
    else
    {
        branch.SupportedLanguages = "en"; // Default to English
    }
    
    ModelState.Remove("UserId");
    ModelState.Remove("User");

    if (ModelState.IsValid)
    {
        if (logo != null && logo.Length > 0)
        {
            branch.Logo = await SaveImage(logo, "logos");
            
            // Extract theme colors from logo
            branch.ThemeColors = await ExtractThemeColors(logo);
        }

        if (banner != null && banner.Length > 0)
        {
            branch.Banner = await SaveImage(banner, "banners");
        }

        _context.Branches.Add(branch);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Branch created successfully!";
        return RedirectToAction("Index", "Dashboard");
    }

    ViewBag.Currencies = CurrencyHelper.GetCurrencies();
    return View(branch);
}

        private async Task<string> ExtractThemeColors(IFormFile logo)
        {
            try
            {
                var colors = await _colorService.ExtractColorsFromImage(logo);
                return System.Text.Json.JsonSerializer.Serialize(colors);
            }
            catch
            {
                // Return default colors on error
                var defaultColors = new
                {
                    primary = "#f6ad55",
                    secondary = "#ed8936",
                    accent = "#dd6b20"
                };
                return System.Text.Json.JsonSerializer.Serialize(defaultColors);
            }
        }



        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            ViewBag.Currencies = CurrencyHelper.GetCurrencies();
            ViewBag.SelectedLanguages = branch.SupportedLanguages.Split(',');
            return View(branch);
        }


        [HttpPost]
public async Task<IActionResult> Edit(Branch branch, IFormFile? logo, IFormFile? banner, string[] selectedLanguages)
{
    var user = await _userManager.GetUserAsync(User);
    var existingBranch = await _context.Branches
        .FirstOrDefaultAsync(b => b.Id == branch.Id && b.UserId == user.Id && !b.IsDeleted);

    if (existingBranch == null)
    {
        return NotFound();
    }

    var nameExists = await _context.Branches
        .AnyAsync(b => b.Name.ToLower() == branch.Name.ToLower() && b.Id != branch.Id && !b.IsDeleted);

    if (nameExists)
    {
        ModelState.AddModelError("Name", "This branch name is already taken.");
    }

    ModelState.Remove("UserId");
    ModelState.Remove("User");

    if (ModelState.IsValid)
    {
        existingBranch.Name = branch.Name;
        existingBranch.Address = branch.Address;
        existingBranch.PhoneNumber = branch.PhoneNumber;
        existingBranch.Currency = branch.Currency;

        // Update supported languages
        if (selectedLanguages != null && selectedLanguages.Length > 0)
        {
            existingBranch.SupportedLanguages = string.Join(",", selectedLanguages);
        }
        else
        {
            existingBranch.SupportedLanguages = "en";
        }

        // Handle logo upload and extract colors
        if (logo != null && logo.Length > 0)
        {
            // Delete old logo
            if (!string.IsNullOrEmpty(existingBranch.Logo))
            {
                DeleteImage(existingBranch.Logo);
            }

            existingBranch.Logo = await SaveImage(logo, "logos");

            // Extract theme colors from new logo
            var colors = await _colorService.ExtractColorsFromImage(logo);
            existingBranch.ThemeColors = System.Text.Json.JsonSerializer.Serialize(colors);
        }

        // Handle banner upload
        if (banner != null && banner.Length > 0)
        {
            // Delete old banner
            if (!string.IsNullOrEmpty(existingBranch.Banner))
            {
                DeleteImage(existingBranch.Banner);
            }

            existingBranch.Banner = await SaveImage(banner, "banners");
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Branch updated successfully!";
        return RedirectToAction("Details", new { id = existingBranch.Id });
    }

    ViewBag.Currencies = CurrencyHelper.GetCurrencies();
    ViewBag.SelectedLanguages = existingBranch.SupportedLanguages.Split(',');
    return View(existingBranch);
}


        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .Include(b => b.Categories)
                    .ThenInclude(c => c.Products)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            return View(branch);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            _context.Branches.Remove(branch);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Branch deleted successfully!";
            return RedirectToAction("Index", "Dashboard");
        }

        private async Task<string> SaveImage(IFormFile file, string folder)
        {
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", folder);
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return $"/images/{folder}/{uniqueFileName}";
        }

        private void DeleteImage(string imagePath)
        {
            try
            {
                var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, imagePath.TrimStart('/'));
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch
            {
                // Ignore errors if file doesn't exist
            }
        }
        
    }