using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>Website hosting settings, bound from "Sites" (env vars Sites__WildcardDomain, ...).</summary>
public class SiteOptions
{
    /// <summary>
    /// Optional shared domain for free subdomain sites: with "myquickmenu.al", every published
    /// site is also at {restaurant}.myquickmenu.al. Needs *.myquickmenu.al and myquickmenu.al
    /// added to Render (two domains, however many restaurants).
    /// </summary>
    public string? WildcardDomain { get; set; }
    /// <summary>More hosts that serve the app itself (comma separated), besides Seo__BaseUrl's, localhost and *.onrender.com.</summary>
    public string? AppHosts { get; set; }
    /// <summary>What a www/subdomain CNAME points to. Default: Render's RENDER_EXTERNAL_HOSTNAME.</summary>
    public string? CnameTarget { get; set; }
    /// <summary>Render's load balancer address for root domains (A record).</summary>
    public string ApexIp { get; set; } = "216.24.57.1";

    /// <summary>Optional: add/verify/remove domains in Render automatically (Account settings → API keys).</summary>
    public string? RenderApiKey { get; set; }
    /// <summary>The web service's id (srv-…), from its dashboard URL.</summary>
    public string? RenderServiceId { get; set; }
    public string RenderApiBase { get; set; } = "https://api.render.com/v1";

    /// <summary>Tests only: where domain checks connect (with the domain as Host header) instead of the domain itself.</summary>
    public string? CheckEndpoint { get; set; }
}

/// <summary>A request host that leads to a restaurant's website.</summary>
/// <summary>
/// <paramref name="WebsiteOn"/> and <paramref name="DomainOn"/>: whether the branch's plan includes the
/// website and own domains (SiteHostMiddleware sends guests to the menu when it doesn't).
/// </summary>
public record SiteTarget(int BranchId, string Slug, string Host, bool IsCustomDomain, bool Verified, bool Published, string? Token,
    bool WebsiteOn = true, bool DomainOn = true);

/// <summary>
/// Which host belongs to which website. Keeps a snapshot of all custom domains and of the
/// subdomain labels for the wildcard domain, refreshed every minute or when a domain or site
/// changes (<see cref="Invalidate"/>), so requests don't hit the database.
/// </summary>
public class SiteHosts
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SiteOptions _o;
    private readonly HashSet<string> _appHosts;
    private readonly string? _wildcard;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Snapshot? _snapshot;

    private record Snapshot(DateTime Built, Dictionary<string, SiteTarget> Domains, Dictionary<string, SiteTarget> Labels,
        Dictionary<int, string> PrimaryHost);

    public SiteHosts(IServiceScopeFactory scopes, IOptions<SiteOptions> options, IConfiguration config)
    {
        _scopes = scopes;
        _o = options.Value;
        _wildcard = SiteRules.NormalizeHost(_o.WildcardDomain);
        _appHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "[::1]" };
        foreach (var h in (_o.AppHosts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) _appHosts.Add(h);
        if (Uri.TryCreate(config["Seo:BaseUrl"], UriKind.Absolute, out var u)) _appHosts.Add(u.Host);
        var render = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_HOSTNAME");
        if (!string.IsNullOrEmpty(render)) _appHosts.Add(render);
        if (_wildcard != null) { _appHosts.Add(_wildcard); _appHosts.Add("www." + _wildcard); }
    }

    public string? WildcardDomain => _wildcard;
    public string? CnameTarget => _o.CnameTarget ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_HOSTNAME");
    public string ApexIp => _o.ApexIp;

    /// <summary>The app's own hosts: its address, localhost, any *.onrender.com, the wildcard root.</summary>
    public bool IsAppHost(string host) =>
        _appHosts.Contains(host) || host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase);

    public void Invalidate() => _snapshot = null;

    private async Task<Snapshot> SnapshotAsync()
    {
        var s = _snapshot;
        if (s != null && DateTime.UtcNow - s.Built < TimeSpan.FromMinutes(1)) return s;
        await _lock.WaitAsync();
        try
        {
            s = _snapshot;
            if (s != null && DateTime.UtcNow - s.Built < TimeSpan.FromMinutes(1)) return s;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sites = await db.Branches.AsNoTracking()
                .Where(b => b.Slug != null)
                .Select(b => new { b.Id, b.Slug, b.UserId, Published = db.BranchSites.Any(x => x.BranchId == b.Id && x.Enabled) })
                .ToListAsync();
            var byId = sites.ToDictionary(x => x.Id);

            // What each branch's plan includes (one load per owner).
            var plans = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            var website = new Dictionary<int, bool>();
            var ownDomain = new Dictionary<int, bool>();
            foreach (var b in sites)
            {
                var e = await plans.ForBranchAsync(b.Id);
                website[b.Id] = e?.Has(BillingModule.Website) == true;
                ownDomain[b.Id] = e?.Has(BillingModule.OwnDomain) == true;
            }
            var domains = await db.Domains.AsNoTracking().OrderBy(d => d.Id).ToListAsync();

            var map = new Dictionary<string, SiteTarget>(StringComparer.OrdinalIgnoreCase);
            var primary = new Dictionary<int, string>();
            foreach (var d in domains.Where(d => byId.ContainsKey(d.BranchId)))
            {
                var b = byId[d.BranchId];
                map[d.Host] = new SiteTarget(d.BranchId, b.Slug, d.Host, true, d.Verified, b.Published, d.Token, website[b.Id], ownDomain[b.Id]);
                if (d.Verified && !primary.ContainsKey(d.BranchId)) primary[d.BranchId] = d.Host;
            }
            var labels = new Dictionary<string, SiteTarget>(StringComparer.OrdinalIgnoreCase);
            if (_wildcard != null)
            {
                // Old links first, so they keep their subdomain (sent on to the current one by
                // SiteHostMiddleware), and a current link always wins over another branch's old one.
                var aliases = await db.BranchSlugAliases.AsNoTracking().Select(a => new { a.BranchId, a.Slug }).ToListAsync();
                foreach (var a in aliases.Where(a => byId.ContainsKey(a.BranchId)))
                {
                    var b = byId[a.BranchId];
                    if (Label(a.Slug) != null && Label(b.Slug) is { } current)
                        labels[Label(a.Slug)!] = new SiteTarget(b.Id, b.Slug, $"{current}.{_wildcard}", false, true, b.Published, null, website[b.Id]);
                }
                foreach (var b in sites)
                {
                    var label = Label(b.Slug);
                    if (label != null) labels[label] = new SiteTarget(b.Id, b.Slug, $"{label}.{_wildcard}", false, true, b.Published, null, website[b.Id]);
                }
            }
            _snapshot = s = new Snapshot(DateTime.UtcNow, map, labels, primary);
            return s;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// A branch slug as a DNS label, or null if it can't be one. Slugs are plain ASCII; old
    /// name-based links may not be ("çajtore" → "xn--ajtore-4ua").
    /// </summary>
    public static string? Label(string slug) => SiteRules.NormalizeHost(slug + ".x") is { } h ? h[..^2] : null;

    /// <summary>The website a request host leads to (verified or not), or null.</summary>
    public async Task<SiteTarget?> ResolveAsync(string host)
    {
        var s = await SnapshotAsync();
        if (s.Domains.TryGetValue(host, out var t)) return t;
        if (_wildcard != null && host.EndsWith("." + _wildcard, StringComparison.OrdinalIgnoreCase))
        {
            var label = host[..^(_wildcard.Length + 1)];
            if (!label.Contains('.') && s.Labels.TryGetValue(label, out var w)) return w;
        }
        return null;
    }

    /// <summary>
    /// The address a published site is best known by: its first verified own domain, else its
    /// subdomain of the wildcard domain, else null (then /site/{slug} on the app).
    /// </summary>
    public async Task<string?> PublicHostAsync(int branchId, string slug)
    {
        var s = await SnapshotAsync();
        if (s.PrimaryHost.TryGetValue(branchId, out var h)) return h;
        return _wildcard != null && Label(slug) is { } label ? $"{label}.{_wildcard}" : null;
    }
}
