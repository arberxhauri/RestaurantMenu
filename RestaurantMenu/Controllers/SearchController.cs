using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Search &amp; sharing (Manager and up): how the restaurant shows up in Google, in link previews
/// and in AI assistants. "Show my menu in Google", what kind of place it is, where it is, its own
/// search title and description (with a live Google preview), the link-preview image, and the
/// "Share &amp; get found" checklist. Admins can also mark a branch as a demo (never indexed).
/// docs/SEO-GEO.md, "How restaurants control their SEO".
/// </summary>
[Authorize(Roles = "OWNER,STAFF,ADMIN")]
[NoIndex]
[Route("branch/{id:int}/search")]
public class SearchController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly SeoService _seo;
    private readonly SiteHosts _hosts;

    public SearchController(ApplicationDbContext context, IBranchAccess access, SeoService seo, SiteHosts hosts)
    {
        _context = context;
        _access = access;
        _seo = seo;
        _hosts = hosts;
    }

    private static string DiskMountPath => Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data";

    private bool IsAdmin => User.IsInRole("ADMIN");

    private async Task<Branch?> BranchAsync(int id, bool tracked = false)
    {
        IQueryable<Branch> q = _context.Branches.Include(b => b.OpeningHours);
        if (!tracked) q = q.AsNoTracking();
        if (IsAdmin) return await q.FirstOrDefaultAsync(b => b.Id == id);
        var allowed = _access.BranchIds(BranchPermission.EditBranch);
        return await q.FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        return View(await PageAsync(branch, null));
    }

    private async Task<SearchPage> PageAsync(Branch b, List<string>? errors)
    {
        var dishes = await _context.Products.CountAsync(p => p.BranchId == b.Id);
        var photos = await _context.Products.CountAsync(p => p.BranchId == b.Id && p.Image != null && p.Image != "");
        var site = await _context.BranchSites.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == b.Id);
        var hasDomain = await _context.Domains.AnyAsync(d => d.BranchId == b.Id && d.Verified);
        var siteOn = site?.Enabled == true && await _access.HasModuleAsync(b.Id, BillingModule.Website);
        var baseLanguage = SeoLanguages.BaseTextLanguage(b);
        var others = SeoLanguages.Of(b).Where(l => l != baseLanguage).ToArray();
        var checklist = ProfileChecklist.For(b, dishes, photos, site, hasDomain, siteOn);
        var mainLanguage = SeoLanguages.Default(b);

        // What Google shows today when nothing is overridden (the generated title and description).
        var categories = await _context.Categories.AsNoTracking().Where(c => c.BranchId == b.Id).OrderBy(c => c.Priority)
            .Select(c => c.Name).Take(5).ToListAsync();
        var hasAllergens = await _context.Products.AnyAsync(p => p.BranchId == b.Id && p.Allergens != null);
        var hasDiets = await _context.Products.AnyAsync(p => p.BranchId == b.Id && p.Diets != Diet.None && p.Diets != Diet.Spicy);
        var generatedTitle = SeoText.MenuTitle(b.Name, b.City, baseLanguage);
        var generatedDescription = SeoText.MenuDescription(new SeoText.MenuFacts(b.Name, b.City, categories, photos > 0, hasAllergens, hasDiets), baseLanguage);

        return new SearchPage(b, baseLanguage, others, mainLanguage,
            MenuUrl: _seo.MenuUrl(b),
            SiteUrl: siteOn ? (await _hosts.PublicHostAsync(b.Id, b.Slug) is { } host ? $"https://{host}/" : _seo.Url($"/site/{b.Slug}")) : null,
            Indexed: SeoIndexing.Menu(b, dishes),
            Dishes: dishes,
            GeneratedTitle: generatedTitle,
            GeneratedDescription: generatedDescription,
            Checklist: checklist,
            IsAdmin: IsAdmin,
            Errors: errors);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [MaxBodySize(6 * 1024 * 1024)]
    public async Task<IActionResult> Save(int id, bool showInSearch, BusinessType businessType, string? cuisine, string? city,
        string? latitude, string? longitude, string? googleBusinessUrl, string? seoTitle, string? seoDescription,
        IFormFile? shareImage, bool removeShareImage, bool? isDemo)
    {
        var branch = await BranchAsync(id, tracked: true);
        if (branch == null) return NotFound();

        var errors = new List<string>();
        var baseLanguage = SeoLanguages.BaseTextLanguage(branch);

        branch.ShowInSearch = showInSearch;
        branch.BusinessType = Enum.IsDefined(businessType) ? businessType : BusinessType.Restaurant;
        branch.Cuisine = Cuisines.Find(cuisine)?.Key;
        branch.City = Clean(city, 100);

        var (lat, lng, coordinateError) = SearchRules.Coordinates(latitude, longitude);
        if (coordinateError != null) errors.Add(coordinateError);
        else { branch.Latitude = lat; branch.Longitude = lng; }

        var google = SearchRules.GoogleBusinessUrl(googleBusinessUrl);
        if (!string.IsNullOrWhiteSpace(googleBusinessUrl) && google == null)
            errors.Add("The Google link should be your Google Maps or Business Profile address, e.g. https://maps.app.goo.gl/… or https://g.page/…");
        else branch.GoogleBusinessUrl = google;

        branch.SeoTitle = Clean(seoTitle, SearchRules.TitleMax);
        branch.SeoDescription = Clean(seoDescription, SearchRules.DescriptionMax);
        Dictionary<string, string> Translations(string field, int max) =>
            SeoLanguages.Of(branch).Where(l => l != baseLanguage)
                .Select(l => (l, v: Clean(Request.Form[$"{field}_{l}"], max)))
                .Where(x => x.v != null).ToDictionary(x => x.l, x => x.v!);
        var titles = Translations("seoTitle", SearchRules.TitleMax);
        var descriptions = Translations("seoDescription", SearchRules.DescriptionMax);
        branch.SeoTitleTranslations = titles.Count == 0 ? null : JsonSerializer.Serialize(titles);
        branch.SeoDescriptionTranslations = descriptions.Count == 0 ? null : JsonSerializer.Serialize(descriptions);

        if (IsAdmin && isDemo != null) branch.IsDemo = isDemo.Value;

        if (shareImage is { Length: > 0 })
        {
            if (!FileUploadHelper.IsValidImage(shareImage)) errors.Add(FileUploadHelper.GetErrorMessage(shareImage));
            else
            {
                DeleteImage(branch.ShareImage);
                branch.ShareImage = await SaveImageAsync(shareImage);
            }
        }
        else if (removeShareImage)
        {
            DeleteImage(branch.ShareImage);
            branch.ShareImage = null;
        }

        if (errors.Count > 0)
        {
            return View("Index", await PageAsync(branch, errors));
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = branch.ShowInSearch && !branch.IsDemo
            ? "Saved. Google and AI assistants will pick up the changes on their next visit."
            : "Saved. This menu is hidden from Google and other search engines.";
        return RedirectToAction(nameof(Index), new { id });
    }

    private static string? Clean(string? value, int max)
    {
        var s = SeoText.OneLine(value);
        if (s.Length == 0) return null;
        return s.Length > max ? s[..max].TrimEnd() : s;
    }

    private async Task<string> SaveImageAsync(IFormFile file)
    {
        var folder = Path.Combine(DiskMountPath, "images", "share");
        Directory.CreateDirectory(folder);
        var name = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using (var stream = new FileStream(Path.Combine(folder, name), FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        return $"/images/share/{name}";
    }

    private static void DeleteImage(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/images/share/")) return;
        try
        {
            var path = Path.Combine(DiskMountPath, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (IOException)
        {
            // An orphaned file costs nothing; the save goes ahead.
        }
    }
}

public record SearchPage(Branch Branch, string BaseLanguage, string[] OtherLanguages, string MainLanguage,
    string MenuUrl, string? SiteUrl, bool Indexed, int Dishes, string GeneratedTitle, string GeneratedDescription,
    ProfileChecklist Checklist, bool IsAdmin, List<string>? Errors);
