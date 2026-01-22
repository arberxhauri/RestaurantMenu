using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Controllers
{
    [Authorize]
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
            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var category = await _context.Categories
                .Include(c => c.Branch)
                .FirstOrDefaultAsync(c => c.Id == id && c.Branch.UserId == user.Id && !c.Branch.IsDeleted);

            if (category == null)
            {
                return NotFound();
            }

            ViewBag.BranchId = category.BranchId;
            ViewBag.BranchName = category.Branch.Name;
            ViewBag.SupportedLanguages = category.Branch.SupportedLanguages.Split(',');
            
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

            if (ModelState.IsValid)
            {
                existingCategory.Name = category.Name;

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

            // Delete all products in this category
            _context.Products.RemoveRange(category.Products);
            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Category and all its products deleted successfully!";
            return RedirectToAction("Details", "Branch", new { id = branchId });
        }

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
