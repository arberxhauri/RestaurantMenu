using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Guest feedback for a branch (Manager and up): every rating with its comment, filters,
/// marking read/resolved, deleting spam, and the settings (on/off, Google review link, who
/// is offered it, email about low ratings).
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/feedback")]
public class FeedbackController : Controller
{
    public const int PageSize = 50;

    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly FeedbackService _feedback;

    public FeedbackController(ApplicationDbContext context, IBranchAccess access, FeedbackService feedback)
    {
        _context = context;
        _access = access;
        _feedback = feedback;
    }

    private Task<Branch?> BranchAsync(int id, BranchPermission p = BranchPermission.ViewInsights)
    {
        var allowed = _access.BranchIds(p);
        return _context.Branches.FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    /// <param name="show">all, low (1-3), high (4-5) or new.</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(int id, string show = "all", int page = 1)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var q = _context.Feedback.AsNoTracking().Where(f => f.BranchId == id);
        q = show switch
        {
            "low" => q.Where(f => f.Rating <= FeedbackRules.LowRating),
            "high" => q.Where(f => f.Rating > FeedbackRules.LowRating),
            "new" => q.Where(f => f.Status == FeedbackStatus.New),
            _ => q
        };
        if (show is not ("low" or "high" or "new")) show = "all";
        var total = await q.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + PageSize - 1) / PageSize));
        var items = await q.OrderByDescending(f => f.CreatedUtc).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        ViewBag.CanSettings = await _access.CanAsync(id, BranchPermission.EditBranch);
        return View(new FeedbackPage(branch, items, show, page, total,
            await _feedback.SummaryAsync(id, DateTime.UtcNow.AddDays(-30)), await _feedback.SummaryAsync(id, null)));
    }

    [HttpPost("{feedbackId:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int id, int feedbackId, string? status, string show = "all")
    {
        if (await BranchAsync(id) == null) return NotFound();
        if (status is not ("New" or "Read" or "Resolved")) return BadRequest();
        var f = await _context.Feedback.FirstOrDefaultAsync(x => x.Id == feedbackId && x.BranchId == id);
        if (f == null) return NotFound();
        f.Status = Enum.Parse<FeedbackStatus>(status);
        await _context.SaveChangesAsync();
        return Redirect(Url.Action(nameof(Index), new { id, show }) + $"#f{feedbackId}");
    }

    /// <summary>Marks everything shown as read.</summary>
    [HttpPost("readall")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll(int id)
    {
        if (await BranchAsync(id) == null) return NotFound();
        var n = await _context.Feedback.Where(f => f.BranchId == id && f.Status == FeedbackStatus.New)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, FeedbackStatus.Read));
        TempData["Success"] = n == 0 ? "Nothing new." : $"{n} marked as read.";
        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpPost("{feedbackId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int feedbackId, string show = "all")
    {
        if (await BranchAsync(id) == null) return NotFound();
        var f = await _context.Feedback.FirstOrDefaultAsync(x => x.Id == feedbackId && x.BranchId == id);
        if (f == null) return NotFound();
        _context.Feedback.Remove(f);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Feedback deleted.";
        return RedirectToAction(nameof(Index), new { id, show });
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(int id, bool enabled, string? googleReviewUrl, bool googleForAll, bool emailOwner)
    {
        var branch = await BranchAsync(id, BranchPermission.EditBranch);
        if (branch == null) return NotFound();
        string? url = null;
        if (!string.IsNullOrWhiteSpace(googleReviewUrl))
        {
            url = FeedbackRules.NormalizeGoogleReviewUrl(googleReviewUrl);
            if (url == null)
            {
                TempData["Error"] = "That isn't a Google review link. Paste the link from your Google Business Profile (Ask for reviews), or your Place ID (starts with ChIJ).";
                return RedirectToAction(nameof(Index), new { id });
            }
        }
        branch.FeedbackEnabled = enabled;
        branch.GoogleReviewUrl = url;
        branch.FeedbackGoogleForAll = googleForAll;
        branch.FeedbackEmailOwner = emailOwner;
        await _context.SaveChangesAsync();
        TempData["Success"] = enabled ? "Feedback settings saved." : "Feedback settings saved. The rating prompt is off on the menu.";
        return RedirectToAction(nameof(Index), new { id });
    }
}

public record FeedbackPage(Branch Branch, IReadOnlyList<Feedback> Items, string Show, int Page, int Total,
    FeedbackService.Summary Last30, FeedbackService.Summary AllTime);
