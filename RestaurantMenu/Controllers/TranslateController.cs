using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// "Fill translations" on the dish and category forms: returns suggestions for the
/// owner to review in the form. Nothing is saved here.
/// </summary>
[Authorize(Roles = "OWNER")]
[NoIndex]
public class TranslateController : Controller
{
    // Which English fields each form may send, matching its translation_{field}_{lang} inputs.
    private static readonly Dictionary<string, string[]> FieldsByKind = new()
    {
        ["dish"] = new[] { "name", "description", "nutritions" },
        ["category"] = new[] { "name" }
    };

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TranslationService _translator;

    public TranslateController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, TranslationService translator)
    {
        _context = context;
        _userManager = userManager;
        _translator = translator;
    }

    public class FillRequest
    {
        public int BranchId { get; set; }
        public string Kind { get; set; } = "";
        public Dictionary<string, string?> Fields { get; set; } = new();
        public List<string> Languages { get; set; } = new();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("translate")]
    public async Task<IActionResult> Fill([FromBody] FillRequest? request, CancellationToken ct)
    {
        if (request == null || !FieldsByKind.TryGetValue(request.Kind, out var allowed))
        {
            return BadRequest(new { message = "Unknown form." });
        }

        var user = await _userManager.GetUserAsync(User);
        var supported = await _context.Branches.AsNoTracking()
            .Where(b => b.Id == request.BranchId && b.UserId == user!.Id && !b.IsDeleted)
            .Select(b => b.SupportedLanguages)
            .FirstOrDefaultAsync(ct);
        if (supported == null)
        {
            return NotFound();
        }

        // Only this branch's menu languages, never English (the source).
        var branchLanguages = supported.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var languages = request.Languages.Distinct().Where(l => l != "en" && branchLanguages.Contains(l)).ToList();
        if (languages.Count == 0)
        {
            return BadRequest(new { message = "This menu has no other languages to translate into." });
        }

        var fields = allowed.ToDictionary(f => f, f => (request.Fields.GetValueOrDefault(f) ?? "").Trim());
        if (fields["name"].Length == 0)
        {
            return BadRequest(new { message = "Write the English name first." });
        }
        if (fields.Values.Any(v => v.Length > TranslationService.MaxFieldLength))
        {
            return BadRequest(new { message = $"Each field can be at most {TranslationService.MaxFieldLength} characters to translate." });
        }

        try
        {
            var translations = await _translator.TranslateAsync(fields, languages, request.Kind == "dish" ? "dish" : "menu category", ct);
            return Json(new { translations });
        }
        catch (TranslationService.TranslationException ex)
        {
            return StatusCode(_translator.IsConfigured ? StatusCodes.Status502BadGateway : StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
    }
}
