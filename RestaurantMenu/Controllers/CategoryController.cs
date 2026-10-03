using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers
{
    [Authorize]
    [NoIndex]
    public class CategoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public CategoryController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Create(int branchId)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.Id == branchId && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            ViewBag.BranchId = branchId;
            ViewBag.BranchName = branch.Name;
            ViewBag.SupportedLanguages = branch.SupportedLanguages.Split(',');
            ViewBag.Schedule = ServingTimes.ToForm(null);
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Category category, IFormCollection form)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.Id == category.BranchId && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            ModelState.Remove("Branch");

            var schedule = ServingTimes.FromForm(form);
            foreach (var error in ServingTimes.Apply(schedule, category))
            {
                ModelState.AddModelError("Schedule", error);
            }

            if (ModelState.IsValid)
            {
                // Set priority to last
                var maxPriority = await _context.Categories
                    .Where(c => c.BranchId == category.BranchId)
                    .MaxAsync(c => (int?)c.Priority) ?? 0;
                category.Priority = maxPriority + 1;

                // Handle translations
                var supportedLanguages = branch.SupportedLanguages.Split(',');
                foreach (var lang in supportedLanguages.Where(l => l != "en"))
                {
                    var translationKey = $"translation_name_{lang}";
                    if (form.ContainsKey(translationKey) && !string.IsNullOrEmpty(form[translationKey]))
                    {
                        category.NameTranslations = TranslationHelper.SetTranslation(
                            category.NameTranslations, lang, form[translationKey]);
                    }
                }

                _context.Categories.Add(category);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Category created successfully!";
                return RedirectToAction("Details", "Branch", new { id = category.BranchId });
            }

            ViewBag.BranchId = category.BranchId;
            ViewBag.BranchName = branch.Name;
            ViewBag.SupportedLanguages = branch.SupportedLanguages.Split(',');
            // Keep what was typed: without this the translation fields come back empty.
            ViewBag.ExistingTranslations = PostedTranslations(form);
            ViewBag.Schedule = schedule;
            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var category = await _context.Categories
                .Include(c => c.Branch)
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == id && c.Branch.UserId == user.Id && !c.Branch.IsDeleted);

            if (category == null)
            {
                return NotFound();
            }

            ViewBag.BranchId = category.BranchId;
            ViewBag.BranchName = category.Branch.Name;
            ViewBag.SupportedLanguages = category.Branch.SupportedLanguages.Split(',');
            ViewBag.Schedule = ServingTimes.ToForm(category);
            
            // Parse existing translations for form
            ViewBag.ExistingTranslations = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(category.NameTranslations))
            {
                try
                {
                    ViewBag.ExistingTranslations = System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, string>>(category.NameTranslations);
                }
                catch { }
            }

            return View(category);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Category category, IFormCollection form)
        {
            var user = await _userManager.GetUserAsync(User);
            var existingCategory = await _context.Categories
                .Include(c => c.Branch)
                .FirstOrDefaultAsync(c => c.Id == category.Id && c.Branch.UserId == user.Id && !c.Branch.IsDeleted);

            if (existingCategory == null)
            {
                return NotFound();
            }

            ModelState.Remove("Branch");

            // Written onto the tracked category, but only saved if everything is valid.
            var schedule = ServingTimes.FromForm(form);
            foreach (var error in ServingTimes.Apply(schedule, existingCategory))
            {
                ModelState.AddModelError("Schedule", error);
            }

            if (ModelState.IsValid)
            {
                existingCategory.Name = category.Name;
                // The edit form has a Position field; it used to be ignored on save.
                existingCategory.Priority = Math.Clamp(category.Priority, 0, 999);

                // Handle translations
                var supportedLanguages = existingCategory.Branch.SupportedLanguages.Split(',');
                existingCategory.NameTranslations = null; // Reset translations
                
                foreach (var lang in supportedLanguages.Where(l => l != "en"))
                {
                    var translationKey = $"translation_name_{lang}";
                    if (form.ContainsKey(translationKey) && !string.IsNullOrEmpty(form[translationKey]))
                    {
                        existingCategory.NameTranslations = TranslationHelper.SetTranslation(
                            existingCategory.NameTranslations, lang, form[translationKey]);
                    }
                }

                await _context.SaveChangesAsync();

                TempData["Success"] = "Category updated successfully!";
                return RedirectToAction("Details", "Branch", new { id = existingCategory.BranchId });
            }

            ViewBag.BranchId = existingCategory.BranchId;
            ViewBag.BranchName = existingCategory.Branch.Name;
            ViewBag.SupportedLanguages = existingCategory.Branch.SupportedLanguages.Split(',');
            // Keep what was typed: without this the translation fields come back empty.
            ViewBag.ExistingTranslations = PostedTranslations(form);
            ViewBag.Schedule = schedule;
            return View(category);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var category = await _context.Categories
                .Include(c => c.Branch)
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == id && c.Branch.UserId == user.Id && !c.Branch.IsDeleted);

            if (category == null)
            {
                return NotFound();
            }

            var branchId = category.BranchId;

            // Soft delete the category and its dishes together (SoftDeleteInterceptor).
            var dishCount = category.Products?.Count ?? 0;
            _context.Products.RemoveRange(category.Products ?? new List<Product>());
            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{category.Name} and its {dishCount} dish(es) were removed from the menu.";
            TempData["UndoUrl"] = Url.Action("Restore", "Category", new { id = category.Id });
            return RedirectToAction("Details", "Branch", new { id = branchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var category = await _context.Categories
                .IgnoreQueryFilters()
                .Include(c => c.Branch)
                .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted
                                          && c.Branch!.UserId == user.Id && !c.Branch.IsDeleted);

            if (category == null || category.DeletedOnUtc == null)
            {
                return NotFound();
            }

            // Bring back the dishes that went with it, not ones deleted separately earlier.
            var from = category.DeletedOnUtc.Value.AddSeconds(-5);
            var to = category.DeletedOnUtc.Value.AddSeconds(5);
            var dishes = await _context.Products
                .IgnoreQueryFilters()
                .Where(p => p.CategoryId == id && p.IsDeleted && p.DeletedOnUtc >= from && p.DeletedOnUtc <= to)
                .ToListAsync();

            foreach (var dish in dishes)
            {
                dish.IsDeleted = false;
                dish.DeletedOnUtc = null;
            }
            category.IsDeleted = false;
            category.DeletedOnUtc = null;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{category.Name} and {dishes.Count} dish(es) are back on the menu.";
            return RedirectToAction("Details", "Branch", new { id = category.BranchId });
        }

        private static Dictionary<string, string> PostedTranslations(IFormCollection form) => form.Keys
            .Where(k => k.StartsWith("translation_name_"))
            .ToDictionary(k => k["translation_name_".Length..], k => form[k].ToString());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePriorities([FromBody] UpdatePrioritiesRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            
            for (int i = 0; i < request.CategoryIds.Count; i++)
            {
                var categoryId = request.CategoryIds[i];
                var category = await _context.Categories
                    .Include(c => c.Branch)
                    .FirstOrDefaultAsync(c => c.Id == categoryId && c.Branch.UserId == user.Id);
                
                if (category != null)
                {
                    category.Priority = i;
                }
            }
            
            await _context.SaveChangesAsync();
            
            return Json(new { success = true });
        }
    }

    public class UpdatePrioritiesRequest
    {
        public List<int> CategoryIds { get; set; }
    }
}
