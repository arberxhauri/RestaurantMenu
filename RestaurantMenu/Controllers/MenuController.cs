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

        // The lookup above is case- and space-insensitive, so one menu is reachable at
        // several spellings. Send every variant to the one canonical URL with a 301 so
        // links and ranking signals accumulate on a single address instead of scattering.
        var canonicalSlug = SeoService.Slug(branch.Name);
        if (!string.Equals(decodedName, canonicalSlug, StringComparison.Ordinal))
        {
            return RedirectPermanent(_seo.MenuUrl(branch.Name, lang));
        }

        ViewBag.CurrencySymbol = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        ViewBag.CurrentLanguage = lang;
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
