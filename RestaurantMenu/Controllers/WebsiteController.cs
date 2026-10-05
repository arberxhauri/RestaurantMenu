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
/// The Website page (Manager and up): publish the branch's website, write its tagline and
/// about text (with translations), social links, and connect the restaurant's own domain.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/website")]
[RequireModule(BillingModule.Website)]
public class WebsiteController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly DomainService _domains;
    private readonly SiteHosts _hosts;
    private readonly SeoService _seo;
    private readonly IEntitlementService _entitlements;

    public WebsiteController(ApplicationDbContext context, IBranchAccess access, DomainService domains, SiteHosts hosts, SeoService seo, IEntitlementService entitlements)
    {
        _entitlements = entitlements;
        _context = context;
        _access = access;
        _domains = domains;
        _hosts = hosts;
        _seo = seo;
    }

    private Task<Branch?> BranchAsync(int id)
    {
        var allowed = _access.BranchIds(BranchPermission.EditBranch);
        return _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    private static string[] OtherLanguages(Branch b) =>
        b.SupportedLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(l => l != "en").ToArray();

    private IActionResult Back(int id, string? anchor = null) => Redirect(Url.Action(nameof(Index), new { id }) + (anchor == null ? "" : "#" + anchor));

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var site = await _context.BranchSites.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == id) ?? new BranchSite { BranchId = id };
        // Domains are kept while the plan doesn't include them, marked paused.
        ViewBag.DomainsPaused = !await _access.HasModuleAsync(id, BillingModule.OwnDomain);
        return View(await PageAsync(branch, site, null));
    }

    private async Task<WebsitePage> PageAsync(Branch branch, BranchSite site, List<string>? errors)
    {
        var slug = branch.Slug;
        var domains = await _context.Domains.AsNoTracking().Where(d => d.BranchId == branch.Id).OrderBy(d => d.Id).ToListAsync();
        var hasHours = branch.HoursEnabled && await _context.BranchHours.AnyAsync(h => h.BranchId == branch.Id);
        return new WebsitePage(branch, site, OtherLanguages(branch), domains,
            SiteUrl: _seo.Url($"/site/{slug}"),
            SubdomainUrl: _hosts.WildcardDomain != null && SiteHosts.Label(slug) is { } label ? $"https://{label}.{_hosts.WildcardDomain}/" : null,
            CnameTarget: _hosts.CnameTarget,
            ApexIp: _hosts.ApexIp,
            RenderApi: _domains.RenderApiConfigured,
            HasHours: hasHours,
            HasBanner: !string.IsNullOrEmpty(branch.Banner),
            Errors: errors);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, bool enabled, string? tagline, string? about, string? instagram, string? facebook, bool showHighlights, bool showMap)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var site = await _context.BranchSites.FirstOrDefaultAsync(s => s.BranchId == id);
        if (site == null)
        {
            site = new BranchSite { BranchId = id };
            _context.BranchSites.Add(site);
        }

        var errors = new List<string>();
        tagline = Clean(tagline, 120, singleLine: true);
        about = Clean(about, 3000, singleLine: false);
        var insta = SiteRules.CleanInstagram(instagram);
        if (!string.IsNullOrWhiteSpace(instagram) && insta == null) errors.Add("The Instagram name can only have letters, numbers, dots and underscores, e.g. yourrestaurant.");
        var fb = SiteRules.CleanFacebook(facebook);
        if (!string.IsNullOrWhiteSpace(facebook) && fb == null) errors.Add("The Facebook link should look like https://facebook.com/yourrestaurant.");

        Dictionary<string, string> Translations(string field, int max, bool singleLine) =>
            OtherLanguages(branch).Select(l => (l, v: Clean(Request.Form[$"{field}_{l}"], max, singleLine)))
                .Where(x => x.v != null).ToDictionary(x => x.l, x => x.v!);
        var taglineTr = Translations("tagline", 120, true);
        var aboutTr = Translations("about", 3000, false);

        site.Enabled = enabled;
        site.Tagline = tagline;
        site.TaglineTranslations = taglineTr.Count == 0 ? null : JsonSerializer.Serialize(taglineTr);
        site.About = about;
        site.AboutTranslations = aboutTr.Count == 0 ? null : JsonSerializer.Serialize(aboutTr);
        site.Instagram = insta;
        site.Facebook = fb;
        site.ShowHighlights = showHighlights;
        site.ShowMap = showMap;
        site.UpdatedUtc = DateTime.UtcNow;

        if (errors.Count > 0)
        {
            // Show what they typed, untouched by the save.
            return View("Index", await PageAsync(branch, site, errors));
        }

        await _context.SaveChangesAsync();
        _hosts.Invalidate();
        TempData["Success"] = enabled ? "Website saved and published." : "Website saved. It's not published, so only your team can see it.";
        return Back(id);
    }

    private static string? Clean(string? text, int max, bool singleLine)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r\n", "\n").Trim();
        t = new string(t.Where(c => c == '\n' ? !singleLine : !char.IsControl(c)).ToArray());
        if (singleLine) t = t.Replace('\n', ' ');
        while (t.Contains("\n\n\n")) t = t.Replace("\n\n\n", "\n\n");
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }

    // ---------------------------------------------------------------- domains

    [HttpPost("domains")]
    [ValidateAntiForgeryToken]
    [RequireModule(BillingModule.OwnDomain)]
    public async Task<IActionResult> AddDomain(int id, string? host)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        // Own domains are counted across the account (legacy: only the per-branch limit).
        var plan = await _entitlements.ForOwnerAsync(branch.UserId);
        if (plan.MaxOwnDomains is { } max
            && await _context.Domains.CountAsync(d => d.Branch!.UserId == branch.UserId) >= max)
        {
            TempData["Error"] = $"Your plan includes {max} own domain{(max == 1 ? "" : "s")}, and they're all in use. Contact us to add more.";
            return Back(id, "domains");
        }
        var (domain, error) = await _domains.AddAsync(id, host, DateTime.UtcNow);
        if (error != null) TempData["Error"] = error;
        else TempData["Success"] = $"{SiteRules.DisplayHost(domain!.Host)} added. Now set its DNS record as shown, then press Check.";
        return Back(id, "domains");
    }

    private async Task<Domain?> DomainAsync(int id, int domainId) =>
        await BranchAsync(id) == null ? null : await _context.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.BranchId == id);

    [HttpPost("domains/{domainId:int}/check")]
    [AllowWhenModuleOff]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckDomain(int id, int domainId)
    {
        var d = await DomainAsync(id, domainId);
        if (d == null) return NotFound();
        var r = await _domains.CheckAsync(d, DateTime.UtcNow);
        TempData[r.Ok ? "Success" : "Warning"] = r.Message;
        return Back(id, "domains");
    }

    [HttpPost("domains/{domainId:int}/delete")]
    [AllowWhenModuleOff] // removing a domain is always allowed
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDomain(int id, int domainId)
    {
        var d = await DomainAsync(id, domainId);
        if (d == null) return NotFound();
        await _domains.RemoveAsync(d);
        TempData["Success"] = $"{SiteRules.DisplayHost(d.Host)} removed. It no longer shows your website.";
        return Back(id, "domains");
    }
}

public record WebsitePage(Branch Branch, BranchSite Site, string[] Languages, IReadOnlyList<Domain> Domains,
    string SiteUrl, string? SubdomainUrl, string? CnameTarget, string ApexIp, bool RenderApi, bool HasHours, bool HasBanner,
    IReadOnlyList<string>? Errors);
