using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER")]
    [NoIndex]
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
            ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
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
    ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
    return View(product);
}


        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var product = await _context.Products
                .Include(p => p.Category)
                .ThenInclude(c => c.Branch)
                .ThenInclude(b => b.Categories)
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
            ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);

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
                .ThenInclude(b => b.Categories)
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
        existingProduct.DisplayOrder = product.DisplayOrder;

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
    ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(existingProduct.Category.Branch.Currency);
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

            // Soft delete (SoftDeleteInterceptor): the photo stays on disk so Undo can bring the dish back whole.
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{product.Name} was removed from the menu.";
            TempData["UndoUrl"] = Url.Action("Restore", "Product", new { id = product.Id });
            return RedirectToAction("Details", "Branch", new { id = branchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var product = await _context.Products
                .IgnoreQueryFilters()
                .Include(p => p.Category)
                    .ThenInclude(c => c!.Branch)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted
                                          && p.Category!.Branch!.UserId == user.Id && !p.Category.Branch.IsDeleted);

            if (product == null)
            {
                return NotFound();
            }

            if (product.Category!.IsDeleted)
            {
                TempData["Error"] = $"{product.Name} can't come back on its own because its category was deleted too. Restore the category instead.";
                return RedirectToAction("Details", "Branch", new { id = product.BranchId });
            }

            product.IsDeleted = false;
            product.DeletedOnUtc = null;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{product.Name} is back on the menu.";
            return RedirectToAction("Details", "Branch", new { id = product.BranchId });
        }

        /// <summary>
        /// Marks a dish available or sold out. The switch on Branch Details sends the state it
        /// now shows (isAvailable), so repeated taps or two open tabs can't drift out of sync
        /// with the server; without it the current state is flipped. Called with fetch it
        /// answers JSON, as a plain form post (no JavaScript) it redirects back to the branch.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAvailability(int id, bool? isAvailable)
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

            product.IsAvailable = isAvailable ?? !product.IsAvailable;
            await _context.SaveChangesAsync();

            var wantsJson = Request.Headers.Accept.Any(a => a != null && a.Contains("application/json"));
            if (wantsJson)
            {
                return Json(new { id = product.Id, isAvailable = product.IsAvailable });
            }

            TempData["Success"] = product.IsAvailable
                ? $"{product.Name} is available again."
                : $"{product.Name} is marked sold out.";
            return RedirectToAction("Details", "Branch", new { id = product.BranchId });
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
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveImage(int id, string? returnTo = null)
        {
            var user = await _userManager.GetUserAsync(User);

            var product = await _context.Products
                .Include(p => p.Category)
                .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id
                                          && p.Category.Branch.UserId == user.Id
                                          && !p.Category.Branch.IsDeleted);

            if (product == null)
                return NotFound();

            if (!string.IsNullOrEmpty(product.Image))
            {
                DeleteImage(product.Image);
                product.Image = null;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Product image deleted successfully!";
            }

            // returnTo = "edit" or "branch"
            if (returnTo == "branch")
                return RedirectToAction("Details", "Branch", new { id = product.BranchId });

            return RedirectToAction("Edit", new { id = product.Id });
        }

    }