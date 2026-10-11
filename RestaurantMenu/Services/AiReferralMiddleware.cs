using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Records visits that AI assistants send to public pages (ReferralVisit), off the request path:
/// the row is written by BackgroundJobs, so a visitor never waits for it.
/// </summary>
public class AiReferralMiddleware
{
    private readonly RequestDelegate _next;

    public AiReferralMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, BackgroundJobs jobs)
    {
        var request = context.Request;
        if (AiReferrals.IsPublicPage(request)
            && AiReferrals.Detect(request.Headers.Referer.ToString(), request.Query["utm_source"].ToString()) is { } hit)
        {
            var visit = new ReferralVisit
            {
                CreatedUtc = DateTime.UtcNow,
                Source = hit.Source,
                Via = hit.Via.Length > 100 ? hit.Via[..100] : hit.Via,
                Host = request.Host.Host.Length > 100 ? request.Host.Host[..100] : request.Host.Host,
                Path = (request.Path.Value ?? "/") is var p && p.Length > 300 ? p[..300] : request.Path.Value ?? "/"
            };
            jobs.Enqueue("ai-referral", async (services, ct) =>
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                db.ReferralVisits.Add(visit);
                await db.SaveChangesAsync(ct);
            });
        }
        return _next(context);
    }
}
