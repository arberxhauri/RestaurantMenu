using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

public class MenuController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly SeoService _seo;

    public MenuController(ApplicationDbContext context, SeoService seo)
    {
        _context = context;
        _seo = seo;
    }

    [Route("menu/{branchName}")]
    public async Task<IActionResult> Index(string branchName, string lang = "en", int? t = null)
    {
        branchName = branchName.Trim();

        var decodedName = Uri.UnescapeDataString(branchName);

        // Read-only: nothing is saved here, and HideSoldOut below trims the loaded dish lists.
        var branch = await _context.Branches
            .AsNoTracking()
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

        // ?t= is the table the guest's QR code was printed for. Anything out of range is
        // ignored rather than shown, so a mistyped link still opens the menu.
        var table = SeoService.ValidTable(t);

        // The lookup above is case- and space-insensitive, so one menu is reachable at
        // several spellings. Send every variant to the one canonical URL with a 301 so
        // links and ranking signals accumulate on a single address instead of scattering.
        var canonicalSlug = SeoService.Slug(branch.Name);
        if (!string.Equals(decodedName, canonicalSlug, StringComparison.Ordinal))
        {
            return RedirectPermanent(_seo.MenuUrl(branch.Name, lang, table));
        }

        // Collected before any are hidden: a guest may have saved a dish to their list
        // earlier, and the list should flag it even when the dish is no longer shown.
        ViewBag.SoldOutIds = (branch.Categories ?? new List<Category>())
            .SelectMany(c => c.Products ?? new List<Product>())
            .Where(p => !p.IsAvailable)
            .Select(p => p.Id)
            .ToList();

        // Sold-out dishes are either shown greyed out (the view handles that) or left out
        // entirely. Drop them before the view and the structured data see them, so a hidden
        // dish isn't still advertised to search engines.
        if (branch.HideSoldOut)
        {
            foreach (var category in branch.Categories ?? new List<Category>())
            {
                category.Products = category.Products?.Where(p => p.IsAvailable).ToList();
            }
        }

        ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.CurrentLanguage = lang;
        ViewBag.Table = table;
        ViewBag.SupportedLanguages = supportedLanguages;
        ViewBag.ThemeColors = branch.ThemeColors;

        ViewData["Seo"] = BuildSeo(branch, lang, supportedLanguages);

        return View(branch);
    }

    private SeoMetadata BuildSeo(Branch branch, string language, string[] supportedLanguages)
    {
        var categoryNames = (branch.Categories ?? new List<Category>())
            .OrderBy(c => c.Priority)
            .Select(c => TranslationHelper.GetTranslation(c.Name, c.NameTranslations, language))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(4)
            .ToList();

        var description = categoryNames.Count > 0
            ? $"The full menu for {branch.Name}: {string.Join(", ", categoryNames)}. Prices and photos, kept up to date by the restaurant."
            : $"The full menu for {branch.Name}. Prices and photos, kept up to date by the restaurant.";

        var image = !string.IsNullOrWhiteSpace(branch.Banner) ? branch.Banner : branch.Logo;

        var alternates = supportedLanguages
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct()
            .Select(l => new SeoAlternate(l, _seo.MenuUrl(branch.Name, l)))
            .ToList();

        return new SeoMetadata
        {
            Title = $"{branch.Name} — Menu",
            Description = description,
            CanonicalUrl = _seo.MenuUrl(branch.Name, language),
            ImageUrl = string.IsNullOrWhiteSpace(image) ? null : _seo.Absolute(image),
            ImageAlt = $"{branch.Name} menu",
            OgType = "restaurant.menu",
            Locale = language,
            FaviconUrl = string.IsNullOrWhiteSpace(branch.Logo) ? "/logo.png" : branch.Logo,
            // A single-language menu gains nothing from hreflang tags pointing at itself.
            Alternates = alternates.Count > 1 ? alternates : Array.Empty<SeoAlternate>(),
            JsonLd = StructuredData.ForBranchMenu(branch, language, _seo)
        };
    }
}
