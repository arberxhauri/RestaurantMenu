using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

// Owners and their staff; what each may do is decided per branch by IBranchAccess.
[Authorize(Roles = "OWNER,STAFF")]
    [NoIndex]
    public class ProductController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IBranchAccess _access;

        public ProductController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            IBranchAccess access)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _access = access;
        }
        
        private string DiskMountPath =>
            Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data";

        [HttpGet]
        public async Task<IActionResult> Create(int branchId)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.EditDishes);
            var branch = await _context.Branches
                .Include(b => b.Categories)
                .FirstOrDefaultAsync(b => b.Id == branchId && allowed.Contains(b.Id) && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            ViewBag.BranchId = branchId;
            ViewBag.BranchName = branch.Name;
            ViewBag.Categories = new SelectList(branch.Categories, "Id", "Name");
            ViewBag.SupportedLanguages = branch.SupportedLanguages.Split(',');
            ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
            ViewBag.Options = new List<ProductOptions.GroupForm>();
            return View();
        }


        [HttpPost]
public async Task<IActionResult> Create(Product product, IFormFile? image, IFormCollection form,
    int[]? allergenFlags, bool allergensNone, int[]? dietFlags)
{
    var user = await _userManager.GetUserAsync(User);
    var allowed = _access.BranchIds(BranchPermission.EditDishes);
    var branch = await _context.Branches
        .Include(b => b.Categories)
        .FirstOrDefaultAsync(b => b.Id == product.BranchId && allowed.Contains(b.Id) && !b.IsDeleted);

    if (branch == null)
    {
        return NotFound();
    }

    ModelState.Remove("Category");
    // The category must be one of this branch's: otherwise a posted id could put the dish
    // into another restaurant's menu.
    if (!branch.Categories!.Any(c => c.Id == product.CategoryId))
    {
        ModelState.AddModelError(nameof(Product.CategoryId), "Choose one of this branch's categories.");
    }
    ApplyDietary(product, allergenFlags, allergensNone, dietFlags);
    // The select only offers known badges; anything else (unreadable or unknown) means none.
    if (ModelState.TryGetValue(nameof(Product.Badge), out var badgeState) && badgeState.Errors.Count > 0) ModelState.Remove(nameof(Product.Badge));
    if (!Enum.IsDefined(product.Badge)) product.Badge = DishBadge.None;
    var optionLanguages = OtherLanguages(branch.SupportedLanguages);
    var optionForm = ProductOptions.FromForm(form, optionLanguages);
    var (optionGroups, optionErrors) = ProductOptions.Validate(optionForm);
    foreach (var error in optionErrors) ModelState.AddModelError("Options", error);

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

        // New dishes go to the end of their category; owners drag them into place on Branch Details.
        product.DisplayOrder = await NextDisplayOrder(product.CategoryId);

        product.OptionGroups = optionGroups;
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
    KeepPostedTranslations(form);
    ViewBag.Options = optionForm;
    return View(product);
}


        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.EditDishes);
            var product = await _context.Products
                .Include(p => p.OptionGroups!).ThenInclude(g => g.Options)
                .Include(p => p.Category)
                .ThenInclude(c => c.Branch)
                .ThenInclude(b => b.Categories)
                .FirstOrDefaultAsync(p => p.Id == id && allowed.Contains(p.Category.BranchId) && !p.Category.Branch.IsDeleted);

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
            ViewBag.Options = ProductOptions.ToForm(product.OptionGroups, OtherLanguages(branch.SupportedLanguages));

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
public async Task<IActionResult> Edit(Product product, IFormFile? image, IFormCollection form,
    int[]? allergenFlags, bool allergensNone, int[]? dietFlags)
{
    var user = await _userManager.GetUserAsync(User);
    var allowed = _access.BranchIds(BranchPermission.EditDishes);
    var existingProduct = await _context.Products
        .Include(p => p.OptionGroups!).ThenInclude(g => g.Options)
        .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
                .ThenInclude(b => b.Categories)
        .FirstOrDefaultAsync(p => p.Id == product.Id && allowed.Contains(p.Category.BranchId) && !p.Category.Branch.IsDeleted);

    if (existingProduct == null)
    {
        return NotFound();
    }

    ModelState.Remove("Category");
    // Moving the dish is only possible within its own branch (see Create).
    if (!existingProduct.Category.Branch.Categories!.Any(c => c.Id == product.CategoryId))
    {
        ModelState.AddModelError(nameof(Product.CategoryId), "Choose one of this branch's categories.");
    }
    ApplyDietary(product, allergenFlags, allergensNone, dietFlags);
    // The select only offers known badges; anything else (unreadable or unknown) means none.
    if (ModelState.TryGetValue(nameof(Product.Badge), out var badgeState) && badgeState.Errors.Count > 0) ModelState.Remove(nameof(Product.Badge));
    if (!Enum.IsDefined(product.Badge)) product.Badge = DishBadge.None;
    var optionLanguages = OtherLanguages(existingProduct.Category.Branch.SupportedLanguages);
    var optionForm = ProductOptions.FromForm(form, optionLanguages);
    var (optionGroups, optionErrors) = ProductOptions.Validate(optionForm);
    foreach (var error in optionErrors) ModelState.AddModelError("Options", error);

    if (ModelState.IsValid)
    {
        // Update basic fields
        existingProduct.Name = product.Name;
        existingProduct.Description = product.Description;
        existingProduct.Nutritions = product.Nutritions;
        existingProduct.Price = product.Price;
        // The order is set by dragging on Branch Details, not by this form. A dish moved to
        // another category goes to the end of it.
        if (existingProduct.CategoryId != product.CategoryId)
        {
            existingProduct.DisplayOrder = await NextDisplayOrder(product.CategoryId);
        }
        existingProduct.CategoryId = product.CategoryId;
        existingProduct.Allergens = product.Allergens;
        existingProduct.Diets = product.Diets;
        existingProduct.IsFeatured = product.IsFeatured;
        existingProduct.Badge = product.Badge;

        // Options are replaced as a whole: the form always posts the full list.
        _context.ProductOptionGroups.RemoveRange(existingProduct.OptionGroups ?? new List<ProductOptionGroup>());
        existingProduct.OptionGroups = optionGroups;

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
    KeepPostedTranslations(form);
    ViewBag.Options = optionForm;
    product.Image = existingProduct.Image; // not posted; keeps the photo in the form and preview
    return View(product);
}

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.DeleteDishes);
            var product = await _context.Products
                .Include(p => p.Category)
                    .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id && allowed.Contains(p.Category.BranchId) && !p.Category.Branch.IsDeleted);

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
                .FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted && !p.Category!.Branch!.IsDeleted);

            // Checked separately: IgnoreQueryFilters above would also switch off the filters
            // inside an access subquery (deleted branches, removed staff).
            if (product == null || !await _access.CanAsync(product.Category!.BranchId, BranchPermission.DeleteDishes))
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
        /// Saves the order of one category's dishes after a drag (or keyboard move) on Branch
        /// Details: DisplayOrder becomes the position in the list. The list must be exactly
        /// the category's current dishes, so a stale page (a dish added, moved or deleted in
        /// another tab) gets 409 instead of silently scrambling the order.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrder([FromBody] UpdateDishOrderRequest? request)
        {
            if (request?.ProductIds == null || request.ProductIds.Count == 0)
            {
                return BadRequest();
            }

            var user = await _userManager.GetUserAsync(User);

            var allowed = _access.BranchIds(BranchPermission.EditDishes);
            var dishes = await _context.Products
                .Where(p => p.CategoryId == request.CategoryId
                            && allowed.Contains(p.Category!.BranchId)
                            && !p.Category.Branch.IsDeleted)
                .ToListAsync();

            if (dishes.Count == 0)
            {
                return NotFound();
            }

            var posted = request.ProductIds;
            if (posted.Distinct().Count() != posted.Count
                || posted.Count != dishes.Count
                || !dishes.All(d => posted.Contains(d.Id)))
            {
                return Conflict(new { message = "This category changed somewhere else. Reload the page to see the latest dishes." });
            }

            var byId = dishes.ToDictionary(d => d.Id);
            for (var i = 0; i < posted.Count; i++)
            {
                byId[posted[i]].DisplayOrder = i;
            }
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        private static string[] OtherLanguages(string supportedLanguages) =>
            supportedLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l != "en").ToArray();

        private async Task<int> NextDisplayOrder(int categoryId) =>
            (await _context.Products
                .Where(p => p.CategoryId == categoryId)
                .MaxAsync(p => (int?)p.DisplayOrder) ?? -1) + 1;

        /// <summary>
        /// Features a dish in the "Recommended" row at the top of the menu, or takes it out.
        /// Same contract as ToggleAvailability: the star sends the state it now shows,
        /// fetch gets JSON, a plain form post is redirected back to the branch.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleFeatured(int id, bool? isFeatured)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.EditDishes);
            var product = await _context.Products
                .Include(p => p.Category)
                    .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id && allowed.Contains(p.Category.BranchId) && !p.Category.Branch.IsDeleted);

            if (product == null)
            {
                return NotFound();
            }

            product.IsFeatured = isFeatured ?? !product.IsFeatured;
            await _context.SaveChangesAsync();

            if (Request.Headers.Accept.Any(a => a != null && a.Contains("application/json")))
            {
                return Json(new { id = product.Id, isFeatured = product.IsFeatured });
            }

            TempData["Success"] = product.IsFeatured
                ? $"{product.Name} is now recommended at the top of the menu."
                : $"{product.Name} is no longer recommended.";
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
            var allowed = _access.BranchIds(BranchPermission.EditDishes);
            var product = await _context.Products
                .Include(p => p.Category)
                    .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id && allowed.Contains(p.Category.BranchId) && !p.Category.Branch.IsDeleted);

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

        /// <summary>
        /// Reads the allergen and diet checkboxes into the dish and checks them. No allergen
        /// ticked and "contains none" unticked means not declared yet (null), which guests
        /// filtering by allergen never see as safe.
        /// </summary>
        private void ApplyDietary(Product product, int[]? allergenFlags, bool allergensNone, int[]? dietFlags)
        {
            var allergens = Dietary.ToAllergens(allergenFlags);
            if (allergensNone && allergens != Allergen.None)
            {
                ModelState.AddModelError(nameof(Product.Allergens),
                    "You ticked \"Contains none of the 14\" and also some allergens. Untick one of them.");
            }

            product.Allergens = allergens != Allergen.None ? allergens : allergensNone ? Allergen.None : null;
            product.Diets = Dietary.ToDiets(dietFlags);

            foreach (var problem in Dietary.Conflicts(product.Allergens, product.Diets))
            {
                ModelState.AddModelError(nameof(Product.Allergens), problem);
            }
        }

        // When the form is shown again after a validation error, refill the translation
        // fields from what was posted. Otherwise they come back empty and saving again
        // would wipe the dish's translations.
        private void KeepPostedTranslations(IFormCollection form)
        {
            Dictionary<string, string> Posted(string field) => form.Keys
                .Where(k => k.StartsWith($"translation_{field}_"))
                .ToDictionary(k => k[$"translation_{field}_".Length..], k => form[k].ToString());

            ViewBag.NameTranslations = Posted("name");
            ViewBag.DescriptionTranslations = Posted("description");
            ViewBag.NutritionTranslations = Posted("nutritions");
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
            var allowed = _access.BranchIds(BranchPermission.EditDishes);

            var product = await _context.Products
                .Include(p => p.Category)
                .ThenInclude(c => c.Branch)
                .FirstOrDefaultAsync(p => p.Id == id
                                          && allowed.Contains(p.Category.BranchId)
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

    public class UpdateDishOrderRequest
    {
        public int CategoryId { get; set; }
        public List<int> ProductIds { get; set; } = new();
    }
