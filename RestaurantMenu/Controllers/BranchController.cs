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

// Owners and their staff; what each may do is decided per branch by IBranchAccess.
[Authorize(Roles = "OWNER,STAFF")]
    [NoIndex]
    public class BranchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ColorExtractionService _colorService;
        private readonly IConfiguration _config;
        private readonly QrCodeService _qr;
        private readonly IBranchAccess _access;
        private readonly FeedbackService _feedback;
        private readonly BranchSlugs _slugs;
        private readonly SiteHosts _hosts;
        private readonly IEntitlementService _entitlements;
        
        public BranchController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            ColorExtractionService colorService,
            IConfiguration config,
            QrCodeService qr,
            IBranchAccess access,
            FeedbackService feedback,
            BranchSlugs slugs,
            SiteHosts hosts,
            IEntitlementService entitlements)
        {
            _entitlements = entitlements;
            _slugs = slugs;
            _hosts = hosts;
            _feedback = feedback;
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _colorService = colorService;
            _config = config;
            _qr = qr;
            _access = access;
        }
        
        private string DiskMountPath =>
            Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data";

        // New branches use the owner's branch quota, so only owners create them.
        [HttpGet]
        [Authorize(Roles = "OWNER")]
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            if (await QuotaProblemAsync(user!.Id) is { } problem)
            {
                TempData["Error"] = problem;
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Currencies = CurrencyHelper.GetCurrencies();
            ViewBag.Hours = OpeningHours.ToForm(null);
            return View();
        }

        [HttpPost]
        [Authorize(Roles = "OWNER")]
public async Task<IActionResult> Create(Branch branch, IFormFile? logo, IFormFile? banner, string[] selectedLanguages, IFormCollection form)
{
    var user = await _userManager.GetUserAsync(User);
    
    // Checked again under the lock when saving (two quick submits can't both get in).
    if (await QuotaProblemAsync(user!.Id) is { } problem)
    {
        TempData["Error"] = problem;
        return RedirectToAction("Index", "Dashboard");
    }

    // Names are free across the platform (two "Oliva" in different cities each get their own
    // link); one owner just can't have two live branches called the same.
    if (await OwnerHasNameAsync(user.Id, branch.Name, null))
    {
        ModelState.AddModelError("Name", "You already have a branch with this name. Add the area, e.g. \"Oliva Blloku\".");
    }

    branch.UserId = user.Id;

    var hoursForm = OpeningHours.FromForm(form);
    var hours = ApplyHours(branch, hoursForm);
    var (brand, brandTouched) = ReadBrand(null, form);
    
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
    // The link is set from the name on the server, never posted.
    ModelState.Remove("Slug");

    if (ModelState.IsValid)
    {
        if (logo != null && logo.Length > 0)
        {
            branch.Logo = await SaveImage(logo, "logos");
            await ApplyLogoColours(brand, logo, brandTouched);
        }
        branch.ThemeColors = brand.ToJson();

        if (banner != null && banner.Length > 0)
        {
            branch.Banner = await SaveImage(banner, "banners");
        }

        branch.OpeningHours = hours;
        await _slugs.AssignAsync(branch);
        var added = await WithinQuotaAsync(user.Id, async () =>
        {
            _context.Branches.Add(branch);
            await _slugs.SaveAsync(branch);
        });
        if (!added)
        {
            _context.Entry(branch).State = EntityState.Detached;
            TempData["Error"] = await QuotaProblemAsync(user.Id) ?? "All your branches are in use.";
            return RedirectToAction("Index", "Dashboard");
        }
        _hosts.Invalidate();

        TempData["Success"] = $"Branch created. Its menu link is /menu/{branch.Slug}.";
        return RedirectToAction("Index", "Dashboard");
    }

    ViewBag.Currencies = CurrencyHelper.GetCurrencies();
    ViewBag.Hours = hoursForm;
    ViewBag.Brand = brand;
    return View(branch);
}

        /// <summary>
        /// Reads the Brand section. Colours must be hex (the pickers always send that);
        /// anything else is a form error and the stored value stays. "Touched" means the
        /// owner changed a colour in this save, which wins over a newly uploaded logo.
        /// </summary>
        private (BrandTheme Theme, bool Touched) ReadBrand(string? storedJson, IFormCollection form)
        {
            var theme = BrandTheme.Parse(storedJson);
            if (!form.ContainsKey("brand_primary"))
            {
                return (theme, false);
            }

            string? Hex(string key) => BrandTheme.NormalizeHex(form[key].ToString());
            var posted = new[] { Hex("brand_primary"), Hex("brand_secondary"), Hex("brand_accent") };
            var shown = new[] { Hex("brand_shown_primary"), Hex("brand_shown_secondary"), Hex("brand_shown_accent") };
            if (posted.Any(c => c == null))
            {
                ModelState.AddModelError("Brand", "Colours must look like #C8642A. Use the colour pickers.");
            }
            else
            {
                (theme.Primary, theme.Secondary, theme.Accent) = (posted[0]!, posted[1]!, posted[2]!);
            }

            var appearance = form["brand_appearance"].ToString();
            var header = form["brand_header"].ToString();
            if (BrandTheme.Appearances.Contains(appearance)) theme.Appearance = appearance;
            if (BrandTheme.Headers.Contains(header)) theme.Header = header;

            return (theme, !posted.SequenceEqual(shown));
        }

        /// <summary>
        /// A new logo: remember its colours (for "Use logo colours") and use them on the menu,
        /// unless the owner chose colours by hand in this same save.
        /// </summary>
        private async Task ApplyLogoColours(BrandTheme theme, IFormFile logo, bool touched)
        {
            try
            {
                var colors = await _colorService.ExtractColorsFromImage(logo);
                var p = BrandTheme.NormalizeHex(colors.Primary);
                var sec = BrandTheme.NormalizeHex(colors.Secondary);
                var a = BrandTheme.NormalizeHex(colors.Accent);
                if (p == null || sec == null || a == null) return;
                (theme.LogoPrimary, theme.LogoSecondary, theme.LogoAccent) = (p, sec, a);
                if (!touched)
                {
                    (theme.Primary, theme.Secondary, theme.Accent) = (p, sec, a);
                }
            }
            catch (Exception)
            {
                // An unreadable image keeps the current colours.
            }
        }


        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.EditBranch);
            var branch = await _context.Branches
                .Include(b => b.OpeningHours)
                .FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id) && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            ViewBag.Currencies = CurrencyHelper.GetCurrencies();
            ViewBag.SelectedLanguages = branch.SupportedLanguages.Split(',');
            ViewBag.Hours = OpeningHours.ToForm(branch.OpeningHours);
            return View(branch);
        }


        [HttpPost]
public async Task<IActionResult> Edit(Branch branch, IFormFile? logo, IFormFile? banner, string[] selectedLanguages, IFormCollection form)
{
    var user = await _userManager.GetUserAsync(User);
    var allowed = _access.BranchIds(BranchPermission.EditBranch);
    var existingBranch = await _context.Branches
        .Include(b => b.OpeningHours)
        .FirstOrDefaultAsync(b => b.Id == branch.Id && allowed.Contains(b.Id) && !b.IsDeleted);

    if (existingBranch == null)
    {
        return NotFound();
    }

    if (await OwnerHasNameAsync(existingBranch.UserId, branch.Name, existingBranch.Id))
    {
        ModelState.AddModelError("Name", "This owner already has a branch with this name. Add the area, e.g. \"Oliva Blloku\".");
    }

    ModelState.Remove("UserId");
    ModelState.Remove("User");
    // The link is set from the name on the server, never posted.
    ModelState.Remove("Slug");

    var hoursForm = OpeningHours.FromForm(form);
    var hours = ApplyHours(branch, hoursForm);
    var (brand, brandTouched) = ReadBrand(existingBranch.ThemeColors, form);

    if (ModelState.IsValid)
    {
        var oldName = existingBranch.Name;
        existingBranch.Name = branch.Name;
        existingBranch.Address = branch.Address;
        existingBranch.PhoneNumber = branch.PhoneNumber;
        existingBranch.Currency = branch.Currency;
        existingBranch.HideSoldOut = branch.HideSoldOut;
        existingBranch.HoursEnabled = branch.HoursEnabled;
        existingBranch.TimeZone = branch.TimeZone;
        // Hours are replaced as a whole: the form always posts all seven days.
        _context.BranchHours.RemoveRange(existingBranch.OpeningHours ?? new List<BranchHours>());
        existingBranch.OpeningHours = hours;

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
            await ApplyLogoColours(brand, logo, brandTouched);
        }
        existingBranch.ThemeColors = brand.ToJson();

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

        // A new name may mean a new link; the old one keeps working (QR codes already printed).
        var oldSlug = await _slugs.RenameAsync(existingBranch, oldName);
        await _slugs.SaveAsync(existingBranch);
        if (oldSlug != null) _hosts.Invalidate();

        TempData["Success"] = oldSlug == null
            ? "Branch updated successfully!"
            : $"Branch updated. Its menu link is now /menu/{existingBranch.Slug}; the old link /menu/{oldSlug} and QR codes already printed still open it.";
        return RedirectToAction("Details", new { id = existingBranch.Id });
    }

    ViewBag.Currencies = CurrencyHelper.GetCurrencies();
    ViewBag.SelectedLanguages = existingBranch.SupportedLanguages.Split(',');
    ViewBag.Hours = hoursForm;
    ViewBag.Brand = brand;
    // Show what was typed, not what is saved.
    existingBranch.HoursEnabled = branch.HoursEnabled;
    existingBranch.TimeZone = branch.TimeZone;
    return View(existingBranch);
}


        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.View);
            var branch = await _context.Branches
                .Include(b => b.Categories)
                    .ThenInclude(c => c.Products)
                .FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id) && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            // The page shows only what this person may do (IBranchAccess); the actions check again.
            var role = await _access.RoleAsync(id);
            ViewBag.Role = role;
            if (BranchAccess.Allows(role, BranchPermission.ManageTeam))
            {
                ViewBag.Team = await _context.BranchMembers
                    .Where(m => m.BranchId == id)
                    .OrderBy(m => m.User!.FullName)
                    .Select(m => new TeamMemberRow(m.Id, m.User!.FullName, m.User.Email!, m.Role, m.User.PasswordHash == null))
                    .ToListAsync();
            }
            if (BranchAccess.Allows(role, BranchPermission.ViewInsights))
            {
                // Guest feedback: the last 30 days in numbers, and the latest few.
                ViewBag.FeedbackSummary = await _feedback.SummaryAsync(id, DateTime.UtcNow.AddDays(-30));
                ViewBag.FeedbackLatest = await _context.Feedback.AsNoTracking().Where(f => f.BranchId == id)
                    .OrderByDescending(f => f.CreatedUtc).Take(5).ToListAsync();
            }

            return View(branch);
        }

        /// <summary>
        /// How guests use this branch's menu: views per day, most opened dishes and the
        /// languages read, over the last 7, 30 or 90 days, compared with the period before.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Insights(int id, [FromServices] MenuInsights insights, int days = 30)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.ViewInsights);
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id) && !b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            if (!MenuInsights.Ranges.Contains(days))
            {
                days = 30;
            }

            return View(new InsightsViewModel
            {
                Branch = branch,
                Report = await insights.ForBranchAsync(branch.Id, days, branch.TimeZone)
            });
        }

        /// <summary>
        /// The QR code for a branch's menu, optionally for one table (?t=). SVG for the web
        /// and design tools, PNG for print shops; download=true saves it as a file.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Qr(int id, int? table = null, string format = "svg", bool download = false)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.Print);
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id) && !b.IsDeleted);

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

            var code = table == null ? null
                : await _context.Tables.Where(t => t.BranchId == branch.Id && t.Number == table).Select(t => t.Code).FirstOrDefaultAsync();
            var link = _qr.MenuLink(branch, table, code);
            var fileName = $"{branch.Slug}-{(table == null ? "menu" : $"table-{table}")}-qr.{format}";

            // Changes when the branch is renamed or the table gets a new ordering code.
            Response.Headers.CacheControl = "private, no-cache";

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
            var allowed = _access.BranchIds(BranchPermission.Print);
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id) && !b.IsDeleted);

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

            // Tables set up for ordering get their code into the link, so the printed card
            // can send orders. The others open the menu with the table number only.
            var numbers = tables.Where(t => t != null).Select(t => t!.Value).ToList();
            var codes = await _context.Tables.AsNoTracking()
                .Where(t => t.BranchId == branch.Id && numbers.Contains(t.Number))
                .ToDictionaryAsync(t => t.Number, t => t.Code);
            if (branch.OrderingEnabled && !noTables && numbers.Count > codes.Count)
            {
                var missing = numbers.Where(n => !codes.ContainsKey(n)).ToList();
                var tablesNotice = $"{TableRanges(missing)} {(missing.Count == 1 ? "isn't" : "aren't")} set up for ordering, so {(missing.Count == 1 ? "its card opens" : "their cards open")} the menu without ordering. Add them on the Tables page first.";
                notice = notice == null ? tablesNotice : $"{notice} {tablesNotice}";
            }

            // One SVG per distinct link: whole-menu copies are all the same code.
            var svgByLink = new Dictionary<string, string>();
            var cards = tables.Select(t =>
            {
                var link = _qr.MenuLink(branch, t, t != null && codes.TryGetValue(t.Value, out var c) ? c : null);
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

        /// <summary>"Table 4", "Tables 4 to 9", "Tables 2, 4 to 9 and 12".</summary>
        public static string TableRanges(IReadOnlyList<int> numbers)
        {
            var parts = new List<string>();
            var sorted = numbers.Distinct().OrderBy(n => n).ToList();
            for (var i = 0; i < sorted.Count;)
            {
                var j = i;
                while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
                parts.Add(j == i ? $"{sorted[i]}" : j == i + 1 ? $"{sorted[i]}, {sorted[j]}" : $"{sorted[i]} to {sorted[j]}");
                i = j + 1;
            }
            var text = parts.Count <= 1 ? parts.FirstOrDefault() ?? "" : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
            return (sorted.Count == 1 ? "Table " : "Tables ") + text;
        }

        // The primary colour extracted from the logo, only if it is a plain hex colour,
        // because it is written into a style attribute.
        // The menu's main colour, for the printed cards' frame.
        private static string BrandColor(string? themeColors) => BrandTheme.Parse(themeColors).Primary;

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var allowed = _access.BranchIds(BranchPermission.DeleteBranch);
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id) && !b.IsDeleted);

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
            // Owner only, checked directly: a deleted branch is invisible to IBranchAccess.
            var branch = await _context.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id && b.IsDeleted);

            if (branch == null)
            {
                return NotFound();
            }

            // Its link was kept while it was deleted (slugs stay reserved), so it comes back as it was.
            if (await OwnerHasNameAsync(user.Id, branch.Name, branch.Id))
            {
                TempData["Error"] = $"{branch.Name} can't be restored because another of your branches now has that name. Rename that one first.";
                return RedirectToAction("Index", "Dashboard");
            }

            var restored = await WithinQuotaAsync(user.Id, async () =>
            {
                branch.IsDeleted = false;
                branch.DeletedOnUtc = null;
                await _context.SaveChangesAsync();
            });
            if (!restored)
            {
                var max = (await _entitlements.ForOwnerAsync(user.Id)).MaxBranches;
                TempData["Error"] = $"{branch.Name} can't be restored: your plan includes {max} branch{(max == 1 ? "" : "es")}, and they're all in use.";
                return RedirectToAction("Index", "Dashboard");
            }
            _hosts.Invalidate();

            TempData["Success"] = $"{branch.Name} is back and its menu is online again.";
            return RedirectToAction("Index", "Dashboard");
        }

        /// <summary>Why this owner can't add a branch right now, or null if they can.</summary>
        private async Task<string?> QuotaProblemAsync(string ownerId)
        {
            var plan = await _entitlements.ForOwnerAsync(ownerId);
            if (!plan.CanWrite) return "New branches can't be added while the back office is read-only.";
            var live = await _context.Branches.CountAsync(b => b.UserId == ownerId);
            return live < plan.MaxBranches ? null
                : $"Your plan includes {plan.MaxBranches} branch{(plan.MaxBranches == 1 ? "" : "es")}, and they're all in use. Contact us to add more.";
        }

        /// <summary>
        /// Adds (or brings back) a branch only while the owner is under their plan's branch count,
        /// safe against two at once: a transaction holding a per-owner advisory lock counts the
        /// live branches, then saves. False (nothing saved) when the count is reached.
        /// </summary>
        private async Task<bool> WithinQuotaAsync(string ownerId, Func<Task> save)
        {
            var max = (await _entitlements.ForOwnerAsync(ownerId)).MaxBranches;
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                var key = "branches:" + ownerId;
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))");
                if (await _context.Branches.CountAsync(b => b.UserId == ownerId) >= max) return false;
                await save();
                await tx.CommitAsync();
                return true;
            });
        }

        /// <summary>Whether this owner already has a live branch with this name (any capitalisation).</summary>
        private Task<bool> OwnerHasNameAsync(string ownerId, string? name, int? exceptId)
        {
            var n = (name ?? "").Trim().ToLower();
            return _context.Branches.AnyAsync(b => b.UserId == ownerId && b.Id != exceptId && b.Name.Trim().ToLower() == n);
        }

        /// <summary>
        /// Validates the opening hours rows and the time zone. Problems only block saving
        /// when hours are switched on; switched off, whatever is valid is kept for later.
        /// </summary>
        private List<BranchHours> ApplyHours(Branch branch, List<OpeningHours.FormDay> hoursForm)
        {
            if (!OpeningHours.IsKnownTimeZone(branch.TimeZone))
            {
                branch.TimeZone = OpeningHours.DefaultTimeZone;
            }

            var (periods, errors) = OpeningHours.Validate(hoursForm);
            if (branch.HoursEnabled)
            {
                foreach (var error in errors)
                {
                    ModelState.AddModelError("Hours", error);
                }
            }
            return periods;
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