using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

public class MenuController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly SeoService _seo;
    private readonly MenuAnalytics _analytics;
    private readonly OrderService _orders;
    private readonly FeedbackService _feedback;

    public MenuController(ApplicationDbContext context, SeoService seo, MenuAnalytics analytics, OrderService orders, FeedbackService feedback)
    {
        _context = context;
        _seo = seo;
        _analytics = analytics;
        _orders = orders;
        _feedback = feedback;
    }

    [Route("menu/{branchName}")]
    public async Task<IActionResult> Index(string branchName, string lang = "en", int? t = null, string? k = null)
    {
        branchName = branchName.Trim();

        var decodedName = Uri.UnescapeDataString(branchName);

        // Read-only: nothing is saved here, and HideSoldOut below trims the loaded dish lists.
        var branch = await _context.Branches
            .AsNoTracking()
            .Include(b => b.Categories.OrderBy(c => c.Priority))
            .ThenInclude(c => c.Products.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id))
            .ThenInclude(p => p.OptionGroups!).ThenInclude(g => g.Options)
            .Include(b => b.OpeningHours)
            // Two collections: separate queries instead of multiplying dish rows by hour rows.
            .AsSplitQuery()
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
        // k is the table's ordering code from its QR code. Only letters and digits are kept,
        // so it can go back into links as is.
        var code = table == null || string.IsNullOrEmpty(k) || k.Length > 12 || !k.All(char.IsAsciiLetterOrDigit) ? null : k;

        // The lookup above is case- and space-insensitive, so one menu is reachable at
        // several spellings. Send every variant to the one canonical URL with a 301 so
        // links and ranking signals accumulate on a single address instead of scattering.
        var canonicalSlug = SeoService.Slug(branch.Name);
        if (!string.Equals(decodedName, canonicalSlug, StringComparison.Ordinal))
        {
            return RedirectPermanent(_seo.MenuUrl(branch.Name, lang, table, code));
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
        ViewBag.TableCode = code;
        ViewBag.Ordering = await OrderingStateAsync(branch, table, code);
        // "Book a table" in the header while online booking is on (not at a table: they're already here).
        var booking = await _context.ReservationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branch.Id);
        if (table == null && booking != null && BookingService.IsBookable(branch, booking))
        {
            ViewBag.BookUrl = $"/book/{canonicalSlug}" + (lang == SeoService.DefaultLanguage ? "" : $"?lang={lang}");
        }
        ViewBag.SupportedLanguages = supportedLanguages;
        ViewBag.ThemeColors = branch.ThemeColors;

        ViewData["Seo"] = BuildSeo(branch, lang, supportedLanguages);

        // Counted only once the page is really served to a guest (not for the redirect above).
        if (MenuAnalytics.ShouldCount(HttpContext))
        {
            await _analytics.RecordAsync(branch.Id, MenuEventType.View, language: lang);
        }

        return View(branch);
    }

    /// <summary>
    /// Anonymous beacons from menu.js (navigator.sendBeacon, form-encoded):
    /// b = branch id, t = dish | add | lang, p = dish id, l = language. Only events that
    /// make sense for that menu are stored; nothing about the guest is.
    /// </summary>
    [HttpPost("menu/event")]
    [EnableRateLimiting("menu-events")]
    public async Task<IActionResult> Event([FromForm] int b, [FromForm] string? t, [FromForm] int? p, [FromForm] string? l)
    {
        MenuEventType? type = t switch
        {
            "dish" => MenuEventType.DishOpen,
            "add" => MenuEventType.AddToList,
            "lang" => MenuEventType.LanguageSwitch,
            _ => null
        };
        if (type == null)
        {
            return BadRequest();
        }

        if (!MenuAnalytics.ShouldCount(HttpContext))
        {
            return NoContent();
        }

        // The query filter already excludes deleted branches and dishes.
        var languages = await _context.Branches.AsNoTracking()
            .Where(x => x.Id == b)
            .Select(x => x.SupportedLanguages)
            .FirstOrDefaultAsync();
        if (languages == null)
        {
            return BadRequest();
        }

        if (type is MenuEventType.DishOpen or MenuEventType.AddToList)
        {
            if (p == null || !await _context.Products.AnyAsync(x => x.Id == p && x.BranchId == b))
            {
                return BadRequest();
            }
        }
        else
        {
            p = null;
        }

        var language = l != null && languages.Split(',', StringSplitOptions.TrimEntries).Contains(l) ? l : null;
        if (type == MenuEventType.LanguageSwitch && language == null)
        {
            return BadRequest();
        }

        await _analytics.RecordAsync(b, type.Value, p, language);
        return NoContent();
    }

    /// <summary>
    /// Whether this page may send orders: "on", or why not ("paused", "closed", "oldcode").
    /// Null when ordering isn't part of this visit (no table, or ordering is off).
    /// </summary>
    private async Task<string?> OrderingStateAsync(Branch branch, int? table, string? code)
    {
        if (table == null || !branch.OrderingEnabled) return null;
        var row = await _context.Tables.AsNoTracking().FirstOrDefaultAsync(x => x.BranchId == branch.Id && x.Number == table);
        if (row == null || !OrderRules.CodeMatches(row.Code, code)) return "oldcode";
        if (branch.OrdersPaused) return "paused";
        if (branch.HoursEnabled && !OpeningHours.GetStatus(branch.OpeningHours ?? new List<BranchHours>(),
                OpeningHours.Zone(branch.TimeZone), DateTime.UtcNow).IsOpen) return "closed";
        return "on";
    }

    /// <summary>
    /// A table's order from menu.js (JSON, see <see cref="OrderRequest"/>). Anonymous: the
    /// table code from the QR code is the permission. Prices come from the menu, never
    /// from the phone. 200 with the order, or 422 with a message in the guest's language.
    /// </summary>
    [HttpPost("menu/order")]
    [EnableRateLimiting("orders")]
    [MaxBodySize(64 * 1024)]
    public async Task<IActionResult> Order([FromBody] OrderRequest? request)
    {
        if (request == null)
        {
            return UnprocessableEntity(new { ok = false, problem = "invalid", message = OrderText.For(null).Changed });
        }

        var result = await _orders.PlaceAsync(request, DateTime.UtcNow);
        if (result.Order == null)
        {
            var problem = result.Problem!.Value.ToString();
            return UnprocessableEntity(new
            {
                ok = false,
                problem = char.ToLowerInvariant(problem[0]) + problem[1..],
                message = result.Message,
                unavailable = result.Unavailable
            });
        }

        var o = result.Order;
        return Ok(new
        {
            ok = true,
            order = new GuestOrder(o.PublicId, o.Number, o.TableNumber, KitchenOrder.StatusId(o.Status),
                OrderText.Status(o.Status, request.Lang), o.Total, o.Items.Sum(i => i.Quantity))
        });
    }

    /// <summary>Status of the guest's own orders: ids=guid,guid (up to 10, from this phone's storage).</summary>
    [HttpGet("menu/orders")]
    [EnableRateLimiting("menu-events")]
    public async Task<IActionResult> Orders(string? ids, string? lang)
    {
        var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().Take(10).ToList();
        Response.Headers.CacheControl = "no-store";
        return Ok(await _orders.GuestStatusAsync(list, lang));
    }

    /// <summary>
    /// A guest's rating from the menu footer (form post from menu.js): b = branch, rating 1-5,
    /// comment, contact (low ratings only), t = table, lang. "website" is a field people never
    /// see (bots fill it) and ms is how long the form was open: either way a bot gets the same
    /// thank-you, and nothing is stored. Answers { ok, google } (the review link to offer, or null).
    /// </summary>
    [HttpPost("menu/feedback")]
    [EnableRateLimiting("feedback")]
    [MaxBodySize(8 * 1024)]
    public async Task<IActionResult> Feedback([FromForm] int b, [FromForm] int rating, [FromForm] string? comment, [FromForm] string? contact,
        [FromForm] int? t, [FromForm] string? lang, [FromForm] string? website, [FromForm] int ms = 0)
    {
        var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == b);
        if (branch == null || !branch.FeedbackEnabled) return NotFound();
        if (rating is < 1 or > 5) return BadRequest();
        var languages = branch.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var language = lang != null && languages.Contains(lang) ? lang : languages.FirstOrDefault() ?? "en";

        if (!string.IsNullOrEmpty(website) || ms < 1500)
        {
            return Ok(new { ok = true, google = (string?)null });
        }
        var result = await _feedback.SubmitAsync(branch, rating, comment, contact, t, language, DateTime.UtcNow);
        return Ok(new { ok = true, google = result.GoogleUrl });
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
