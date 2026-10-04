using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

public record FeedbackResult(bool Saved, string? GoogleUrl);

/// <summary>
/// Guest feedback from the menu: stores it, emails the owner about low ratings (private,
/// so they can make it right), and says whether to offer the Google review link.
/// </summary>
public class FeedbackService
{
    private readonly ApplicationDbContext _db;
    private readonly EmailService _email;
    private readonly EmailQueue _queue;
    private readonly SeoService _seo;

    public FeedbackService(ApplicationDbContext db, EmailService email, EmailQueue queue, SeoService seo)
    {
        _db = db;
        _email = email;
        _queue = queue;
        _seo = seo;
    }

    /// <summary>The Google link to offer after this rating, if any.</summary>
    public static string? GoogleFor(Branch b, int rating) =>
        b.GoogleReviewUrl != null && (b.FeedbackGoogleForAll || rating > FeedbackRules.LowRating) ? b.GoogleReviewUrl : null;

    public async Task<FeedbackResult> SubmitAsync(Branch branch, int rating, string? comment, string? contact, int? table, string language, DateTime utcNow)
    {
        var f = new Feedback
        {
            BranchId = branch.Id,
            Rating = rating,
            Comment = FeedbackRules.Clean(comment, FeedbackRules.MaxComment),
            // A way to reach the guest is only asked for (and kept) after a low rating.
            Contact = rating <= FeedbackRules.LowRating ? FeedbackRules.Clean(contact, FeedbackRules.MaxContact)?.Replace('\n', ' ') : null,
            TableNumber = SeoService.ValidTable(table),
            Language = language,
            Status = FeedbackStatus.New,
            CreatedUtc = utcNow
        };
        _db.Feedback.Add(f);
        await _db.SaveChangesAsync();

        if (rating <= FeedbackRules.LowRating && branch.FeedbackEmailOwner && _email.IsConfigured)
        {
            var owner = await _db.Users.AsNoTracking().Where(u => u.Id == branch.UserId).Select(u => new { u.Email, u.FullName }).FirstOrDefaultAsync();
            if (owner?.Email != null)
            {
                var lines = new List<string> { $"A guest rated their visit to {branch.Name} {rating} out of 5{(f.TableNumber != null ? $" (table {f.TableNumber})" : "")}." };
                lines.Add(f.Comment != null ? $"They wrote: \"{f.Comment}\"" : "They didn't leave a comment.");
                if (f.Contact != null) lines.Add($"They'd like to hear back: {f.Contact}");
                var (html, text) = EmailTemplate.Render(owner.FullName, lines[0], lines, "Open feedback", _seo.Url($"/branch/{branch.Id}/feedback"),
                    "Only you and your managers see this; it isn't public.");
                _queue.Enqueue(new EmailMessage(owner.Email, owner.FullName, $"{rating}★ feedback at {branch.Name}", html, text));
            }
        }
        return new FeedbackResult(true, GoogleFor(branch, rating));
    }

    public record Summary(int Count, double Average, int[] ByStars, int Unread);

    /// <summary>Count, average and stars 1..5 for a period (all time when <paramref name="sinceUtc"/> is null).</summary>
    public async Task<Summary> SummaryAsync(int branchId, DateTime? sinceUtc)
    {
        var q = _db.Feedback.AsNoTracking().Where(f => f.BranchId == branchId);
        if (sinceUtc != null) q = q.Where(f => f.CreatedUtc >= sinceUtc);
        var groups = await q.GroupBy(f => f.Rating).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var by = new int[6];
        foreach (var g in groups) if (g.Key is >= 1 and <= 5) by[g.Key] = g.Count;
        var count = by.Sum();
        var unread = await _db.Feedback.CountAsync(f => f.BranchId == branchId && f.Status == FeedbackStatus.New);
        return new Summary(count, count == 0 ? 0 : Math.Round(Enumerable.Range(1, 5).Sum(s => s * by[s]) / (double)count, 1), by, unread);
    }
}
