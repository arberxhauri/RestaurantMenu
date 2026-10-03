using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;
using RestaurantMenu.ViewModels;
using System.Text.Json;
using System.Text.RegularExpressions;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER")]
    [NoIndex]
    public class BranchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ColorExtractionService _colorService;
        private readonly IConfiguration _config;
        private readonly QrCodeService _qr;
        
        public BranchController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            ColorExtractionService colorService,
            IConfiguration config,
            QrCodeService qr)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _colorService = colorService;
            _config = config;
            _qr = qr;
        }
        
        private string DiskMountPath =>
            Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data";

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
        existingBranch.HideSoldOut = branch.HideSoldOut;

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

        /// <summary>
        /// The QR code for a branch's menu, optionally for one table (?t=). SVG for the web
        /// and design tools, PNG for print shops; download=true saves it as a file.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Qr(int id, int? table = null, string format = "svg", bool download = false)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            if (table != null && SeoService.ValidTable(table) == null)
            {
                return BadRequest($"Table numbers go from 1 to {SeoService.MaxTable}.");
            }

            format = format.ToLowerInvariant();
            if (format != "svg" && format != "png")
            {
                return BadRequest("Format must be svg or png.");
            }

            var link = _qr.MenuLink(branch, table);
            var fileName = $"{SeoService.Slug(branch.Name)}-{(table == null ? "menu" : $"table-{table}")}-qr.{format}";

            // The code changes only when the branch is renamed, so let the browser keep it for a while.
            Response.Headers.CacheControl = "private, max-age=3600";

            var (bytes, contentType) = format == "png"
                ? (_qr.Png(link), "image/png")
                : (System.Text.Encoding.UTF8.GetBytes(_qr.Svg(link)), "image/svg+xml");

            return download ? File(bytes, contentType, fileName) : File(bytes, contentType);
        }

        /// <summary>
        /// Printable table tents (two A6 tents per A4 page) or sticker sheets (twelve per A4
        /// page), one QR code per table. All options are query parameters, so a print setup
        /// can be bookmarked and the page works without JavaScript.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Print(int id, string layout = PrintViewModel.Tent, int from = 1, int to = 10,
            bool noTables = false, int copies = 0, string? lang = null, string? headline = null)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            layout = layout == PrintViewModel.Sticker ? PrintViewModel.Sticker : PrintViewModel.Tent;
            var perPage = layout == PrintViewModel.Tent ? 2 : 12;

            var languages = branch.SupportedLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (languages.Length == 0) languages = new[] { "en" };
            var language = lang != null && languages.Contains(lang) ? lang : languages[0];

            headline = string.IsNullOrWhiteSpace(headline) ? TableText.ScanPrompt(language) : headline.Trim();
            if (headline.Length > 60) headline = headline[..60];

            // Correct the range instead of failing: a print page should always show something.
            string? notice = null;
            var tables = new List<int?>();
            if (noTables)
            {
                if (copies < 1) copies = perPage; // one full page
                if (copies > PrintViewModel.MaxCards) { copies = PrintViewModel.MaxCards; notice = $"Limited to {PrintViewModel.MaxCards} copies per print."; }
                tables.AddRange(Enumerable.Repeat<int?>(null, copies));
            }
            else
            {
                from = Math.Clamp(from, 1, SeoService.MaxTable);
                to = Math.Clamp(to, 1, SeoService.MaxTable);
                if (to < from) (from, to) = (to, from);
                if (to - from + 1 > PrintViewModel.MaxCards)
                {
                    to = from + PrintViewModel.MaxCards - 1;
                    notice = $"Up to {PrintViewModel.MaxCards} tables per print, so this shows tables {from} to {to}. Print the rest in a second run.";
                }
                for (var t = from; t <= to; t++) tables.Add(t);
            }

            // One SVG per distinct link: whole-menu copies are all the same code.
            var svgByLink = new Dictionary<string, string>();
            var cards = tables.Select(t =>
            {
                var link = _qr.MenuLink(branch, t);
                if (!svgByLink.TryGetValue(link, out var svg))
                {
                    svg = _qr.Svg(link);
                    svgByLink[link] = svg;
                }
                return new PrintCard(t, t == null ? null : TableText.Label(t.Value, language), svg, link);
            }).ToList();

            var menuUrl = _qr.MenuLink(branch, null);

            return View(new PrintViewModel
            {
                Branch = branch,
                Layout = layout,
                From = from,
                To = to,
                NoTables = noTables,
                Copies = noTables ? copies : perPage,
                Language = language,
                Languages = languages,
                Headline = headline,
                ShortUrl = Regex.Replace(menuUrl, "^https?://", ""),
                BrandColor = BrandColor(branch.ThemeColors),
                Notice = notice,
                Cards = cards
            });
        }

        // The primary colour extracted from the logo, only if it is a plain hex colour,
        // because it is written into a style attribute.
        private static string BrandColor(string? themeColors)
        {
            const string fallback = "#C8642A";
            if (string.IsNullOrEmpty(themeColors)) return fallback;
            try
            {
                var colors = JsonSerializer.Deserialize<Dictionary<string, string>>(themeColors);
                var primary = colors?.GetValueOrDefault("Primary") ?? colors?.GetValueOrDefault("primary");
                return primary != null && Regex.IsMatch(primary, "^#[0-9a-fA-F]{3,8}$") ? primary : fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
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

            // Soft delete (SoftDeleteInterceptor): the menu goes offline but nothing is erased.
            _context.Branches.Remove(branch);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{branch.Name} was deleted and its menu is offline.";
            TempData["UndoUrl"] = Url.Action("Restore", "Branch", new { id = branch.Id });
            return RedirectToAction("Index", "Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var branch = await _context.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            var liveCount = await _context.Branches.CountAsync(b => b.UserId == user.Id);
            if (liveCount >= user.NumberOfBranches)
            {
                TempData["Error"] = $"{branch.Name} can't be restored: all your branch slots are in use.";
                return RedirectToAction("Index", "Dashboard");
            }

            var nameTaken = await _context.Branches.AnyAsync(b => b.Name.ToLower() == branch.Name.ToLower());
            if (nameTaken)
            {
                TempData["Error"] = $"{branch.Name} can't be restored because another branch now uses that name.";
                return RedirectToAction("Index", "Dashboard");
            }

            branch.IsDeleted = false;
            branch.DeletedOnUtc = null;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{branch.Name} is back and its menu is online again.";
            return RedirectToAction("Index", "Dashboard");
        }

        private async Task<string> SaveImage(IFormFile file, string folder)
        {
            // Save to persistent disk: /var/data/images/<folder>/
            var uploadsFolder = Path.Combine(DiskMountPath, "images", folder);
            Directory.CreateDirectory(uploadsFolder);

            var safeFileName = Path.GetFileName(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}_{safeFileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // Public URL served by Program.cs static files mapping
            return $"/images/{folder}/{uniqueFileName}";
        }

        private void DeleteImage(string imageUrl)
        {
            try
            {
                // imageUrl is like "/images/logos/abc.png"
                var relative = imageUrl.TrimStart('/'); // "images/logos/abc.png"
                if (!relative.StartsWith("images/")) return;

                var fullPath = Path.Combine(DiskMountPath, relative.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch
            {
                // ignore
            }
        }
        
    }