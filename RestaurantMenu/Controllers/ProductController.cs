using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER")]
    public class ProductController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
        }
        
        private string DiskMountPath =>
            Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data";

        [HttpGet]
        public async Task<IActionResult> Create(int branchId)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .Include(b => b.Categories)
                .FirstOrDefaultAsync(b => b.Id == branchId && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            ViewBag.BranchId = branchId;
            ViewBag.BranchName = branch.Name;
            ViewBag.Categories = new SelectList(branch.Categories, "Id", "Name");
            ViewBag.SupportedLanguages = branch.SupportedLanguages.Split(',');
            return View();
        }


        [HttpPost]
public async Task<IActionResult> Create(Product product, IFormFile? image, IFormCollection form)
{
    var user = await _userManager.GetUserAsync(User);
    var branch = await _context.Branches
        .Include(b => b.Categories)
        .FirstOrDefaultAsync(b => b.Id == product.BranchId && b.UserId == user.Id && !b.IsDeleted);

    if (branch == null)
    {
        return NotFound();
    }

    ModelState.Remove("Category");

    if (ModelState.IsValid)
    {
        // Handle image upload
        if (image != null && image.Length > 0)
        {
            product.Image = await SaveImage(image);
        }

        // Handle translations
        var supportedLanguages = branch.SupportedLanguages.Split(',');
        foreach (var lang in supportedLanguages.Where(l => l != "en"))
        {
            var nameKey = $"translation_name_{lang}";
            var descKey = $"translation_description_{lang}";
            var nutritionKey = $"translation_nutritions_{lang}";

            if (form.ContainsKey(nameKey) && !string.IsNullOrEmpty(form[nameKey]))
            {
                product.NameTranslations = TranslationHelper.SetTranslation(
                    product.NameTranslations, lang, form[nameKey]);
            }

            if (form.ContainsKey(descKey) && !string.IsNullOrEmpty(form[descKey]))
            {
                product.DescriptionTranslations = TranslationHelper.SetTranslation(
                    product.DescriptionTranslations, lang, form[descKey]);
            }

            if (form.ContainsKey(nutritionKey) && !string.IsNullOrEmpty(form[nutritionKey]))
            {
                product.NutritionsTranslations = TranslationHelper.SetTranslation(
                    product.NutritionsTranslations, lang, form[nutritionKey]);
            }
        }

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Product created successfully!";
        return RedirectToAction("Details", "Branch", new { id = product.BranchId });
    }

    ViewBag.BranchId = product.BranchId;
    ViewBag.BranchName = branch.Name;
    ViewBag.Categories = new SelectList(branch.Categories, "Id", "Name", product.CategoryId);
    ViewBag.SupportedLanguages = branch.SupportedLanguages.Split(',');
    return View(product);
}


        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var product = await _context.Products
                .Include(p => p.Category)
                .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id && p.Category.Branch.UserId == user.Id && !p.Category.Branch.IsDeleted);

            if (product == null)
            {
                return NotFound();
            }

            var branch = product.Category.Branch;
            ViewBag.BranchId = branch.Id;
            ViewBag.BranchName = branch.Name;
            ViewBag.Categories = new SelectList(branch.Categories, "Id", "Name", product.CategoryId);
            ViewBag.SupportedLanguages = branch.SupportedLanguages.Split(',');

            // Parse existing translations
            ViewBag.NameTranslations = ParseTranslations(product.NameTranslations);
            ViewBag.DescriptionTranslations = ParseTranslations(product.DescriptionTranslations);
            ViewBag.NutritionTranslations = ParseTranslations(product.NutritionsTranslations);

            return View(product);
        }

        private Dictionary<string, string> ParseTranslations(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, string>();

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }


       [HttpPost]
public async Task<IActionResult> Edit(Product product, IFormFile? image, IFormCollection form)
{
    var user = await _userManager.GetUserAsync(User);
    var existingProduct = await _context.Products
        .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
        .FirstOrDefaultAsync(p => p.Id == product.Id && p.Category.Branch.UserId == user.Id && !p.Category.Branch.IsDeleted);

    if (existingProduct == null)
    {
        return NotFound();
    }

    ModelState.Remove("Category");

    if (ModelState.IsValid)
    {
        // Update basic fields
        existingProduct.Name = product.Name;
        existingProduct.Description = product.Description;
        existingProduct.Nutritions = product.Nutritions;
        existingProduct.Price = product.Price;
        existingProduct.CategoryId = product.CategoryId;

        // Handle image upload
        if (image != null && image.Length > 0)
        {
            // Delete old image if exists
            if (!string.IsNullOrEmpty(existingProduct.Image))
            {
                DeleteImage(existingProduct.Image);
            }
            existingProduct.Image = await SaveImage(image);
        }

        // Handle translations
        var supportedLanguages = existingProduct.Category.Branch.SupportedLanguages.Split(',');
        existingProduct.NameTranslations = null;
        existingProduct.DescriptionTranslations = null;
        existingProduct.NutritionsTranslations = null;

        foreach (var lang in supportedLanguages.Where(l => l != "en"))
        {
            var nameKey = $"translation_name_{lang}";
            var descKey = $"translation_description_{lang}";
            var nutritionKey = $"translation_nutritions_{lang}";

            if (form.ContainsKey(nameKey) && !string.IsNullOrEmpty(form[nameKey]))
            {
                existingProduct.NameTranslations = TranslationHelper.SetTranslation(
                    existingProduct.NameTranslations, lang, form[nameKey]);
            }

            if (form.ContainsKey(descKey) && !string.IsNullOrEmpty(form[descKey]))
            {
                existingProduct.DescriptionTranslations = TranslationHelper.SetTranslation(
                    existingProduct.DescriptionTranslations, lang, form[descKey]);
            }

            if (form.ContainsKey(nutritionKey) && !string.IsNullOrEmpty(form[nutritionKey]))
            {
                existingProduct.NutritionsTranslations = TranslationHelper.SetTranslation(
                    existingProduct.NutritionsTranslations, lang, form[nutritionKey]);
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Product updated successfully!";
        return RedirectToAction("Details", "Branch", new { id = existingProduct.BranchId });
    }

    var branch = existingProduct.Category.Branch;
    ViewBag.BranchId = branch.Id;
    ViewBag.BranchName = branch.Name;
    ViewBag.Categories = new SelectList(branch.Categories, "Id", "Name", product.CategoryId);
    ViewBag.SupportedLanguages = existingProduct.Category.Branch.SupportedLanguages.Split(',');
    return View(product);
}

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var product = await _context.Products
                .Include(p => p.Category)
                    .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id && p.Category.Branch.UserId == user.Id && !p.Category.Branch.IsDeleted);

            if (product == null)
            {
                return NotFound();
            }

            var branchId = product.BranchId;

            if (!string.IsNullOrEmpty(product.Image))
            {
                DeleteImage(product.Image);
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Product deleted successfully!";
            return RedirectToAction("Details", "Branch", new { id = branchId });
        }

        private async Task<string> SaveImage(IFormFile file)
        {
            var uploadsFolder = Path.Combine(DiskMountPath, "images", "products");
            Directory.CreateDirectory(uploadsFolder);

            var safeFileName = Path.GetFileName(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}_{safeFileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return $"/images/products/{uniqueFileName}";
        }

        private void DeleteImage(string imageUrl)
        {
            var relative = imageUrl.TrimStart('/');
            if (!relative.StartsWith("images/")) return;

            var fullPath = Path.Combine(DiskMountPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
    }