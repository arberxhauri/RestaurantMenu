using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>IndexNow settings, bound from "IndexNow" (env var IndexNow__Key).</summary>
public partial class IndexNowOptions
{
    /// <summary>8-128 letters, digits or dashes; served at /{Key}.txt on every host. Empty = off.</summary>
    public string? Key { get; set; }
    public string Endpoint { get; set; } = "https://api.indexnow.org/indexnow";
    /// <summary>The same URL is pinged at most once in this time; later changes wait for it.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMinutes(10);

    public bool Enabled => Key != null && KeyFormat().IsMatch(Key);

    [GeneratedRegex("^[A-Za-z0-9-]{8,128}$")]
    private static partial Regex KeyFormat();
}

/// <summary>
/// IndexNow (docs/SEO-GEO.md, "IndexNow"): tells Bing (whose index feeds ChatGPT search and
/// Copilot), Yandex, Seznam and Naver which URLs changed, so they recrawl them within minutes
/// instead of days. Fed by SeoChanges (a restaurant's menu, website, hours or link changed) and,
/// once per start, the marketing pages (a deploy). Each URL is pinged at most once per
/// <see cref="IndexNowOptions.Debounce"/>; a change inside that window is sent when it ends.
/// </summary>
public class IndexNowQueue
{
    private readonly ConcurrentDictionary<int, byte> _branches = new();
    private readonly ConcurrentDictionary<string, byte> _urls = new();

    public IndexNowQueue(SeoChanges changes, IOptions<IndexNowOptions> options)
    {
        if (options.Value.Enabled) changes.BranchesChanged += ids => { foreach (var id in ids) _branches[id] = 0; };
    }

    public void Enqueue(IEnumerable<string> urls)
    {
        foreach (var u in urls) _urls[u] = 0;
    }

    /// <summary>What has been queued since the last call.</summary>
    public (List<int> Branches, List<string> Urls) Take()
    {
        var b = _branches.Keys.ToList();
        foreach (var id in b) _branches.TryRemove(id, out _);
        var u = _urls.Keys.ToList();
        foreach (var url in u) _urls.TryRemove(url, out _);
        return (b, u);
    }
}

/// <summary>Sends the queued URLs every 30 seconds, debounced per URL, grouped by host.</summary>
public class IndexNowWorker : BackgroundService
{
    private readonly IndexNowQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly IndexNowOptions _options;
    private readonly ILogger<IndexNowWorker> _logger;
    private readonly Dictionary<string, DateTime> _lastSent = new();
    private readonly Dictionary<string, DateTime> _waiting = new();

    public IndexNowWorker(IndexNowQueue queue, IServiceScopeFactory scopes, IHttpClientFactory http, IOptions<IndexNowOptions> options, ILogger<IndexNowWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("IndexNow: off (set IndexNow__Key to switch it on)");
            return;
        }

        // A start is a deploy: the marketing pages may have changed.
        using (var scope = _scopes.CreateScope())
        {
            var seo = scope.ServiceProvider.GetRequiredService<SeoService>();
            if (IsPublic(seo.BaseUrl))
                _queue.Enqueue(MarketingPages.All.SelectMany(p => p.Languages.Select(l => seo.Url(MarketingPages.PathFor(p, l)))));
            else
                _logger.LogInformation("IndexNow: Seo__BaseUrl isn't a public https address, so nothing is sent");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "IndexNow: run failed; will retry");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static bool IsPublic(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) && u.Scheme == "https" && !u.IsLoopback && u.Host.Contains('.');

    private async Task RunOnceAsync(DateTime now, CancellationToken ct)
    {
        var (branches, urls) = _queue.Take();
        if (branches.Count > 0)
        {
            using var scope = _scopes.CreateScope();
            urls.AddRange(await BranchUrlsAsync(scope.ServiceProvider, branches));
        }

        // Debounce: a URL sent less than Debounce ago waits until the window ends.
        foreach (var url in urls.Distinct())
        {
            var due = _lastSent.TryGetValue(url, out var last) ? last + _options.Debounce : now;
            _waiting[url] = _waiting.TryGetValue(url, out var already) ? (already < due ? already : due) : due;
        }
        var ready = _waiting.Where(w => w.Value <= now).Select(w => w.Key).ToList();
        if (ready.Count == 0) return;

        foreach (var group in ready.GroupBy(u => new Uri(u).Host))
        {
            if (!IsPublic($"https://{group.Key}")) continue;
            foreach (var batch in group.Chunk(10_000))
            {
                var body = new
                {
                    host = group.Key,
                    key = _options.Key,
                    keyLocation = $"https://{group.Key}/{_options.Key}.txt",
                    urlList = batch
                };
                var response = await _http.CreateClient(nameof(IndexNowWorker)).PostAsJsonAsync(_options.Endpoint, body, ct);
                // 200 OK and 202 Accepted are both success; 422 means a URL isn't on this host.
                if (response.IsSuccessStatusCode)
                    _logger.LogInformation("IndexNow: sent {Count} URL(s) for {Host}", batch.Length, group.Key);
                else
                    _logger.LogWarning("IndexNow: {Host} answered {Status}", group.Key, (int)response.StatusCode);
            }
        }
        foreach (var url in ready)
        {
            _waiting.Remove(url);
            _lastSent[url] = now;
        }
        // Forget old entries so the dictionary doesn't grow forever.
        foreach (var old in _lastSent.Where(x => now - x.Value > _options.Debounce).Select(x => x.Key).ToList()) _lastSent.Remove(old);
    }

    /// <summary>Every public address of these branches: the menu in each language, and the website if published.</summary>
    public static async Task<List<string>> BranchUrlsAsync(IServiceProvider services, IReadOnlyCollection<int> ids)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var seo = services.GetRequiredService<SeoService>();
        var hosts = services.GetRequiredService<SiteHosts>();
        var urls = new List<string>();
        // Deleted branches too: their URLs now answer 404, which search engines should hear about.
        var branches = await db.Branches.IgnoreQueryFilters().AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync();
        var published = await db.BranchSites.Where(s => ids.Contains(s.BranchId) && s.Enabled).Select(s => s.BranchId).ToListAsync();
        foreach (var b in branches)
        {
            if (string.IsNullOrEmpty(b.Slug)) continue;
            var main = SeoLanguages.Default(b);
            foreach (var l in SeoLanguages.Of(b)) urls.Add(seo.MenuUrl(b, l));
            if (published.Contains(b.Id))
            {
                var host = await hosts.PublicHostAsync(b.Id, b.Slug);
                foreach (var l in SeoLanguages.Of(b))
                    urls.Add((host != null ? $"https://{host}/" : seo.Url($"/site/{b.Slug}")) + SeoLanguages.Query(l, main));
            }
        }
        // The sitemaps and the directory list these restaurants too.
        urls.Add(seo.Url("/restaurants"));
        return urls;
    }
}

/// <summary>Answers /{key}.txt with the IndexNow key on every host, restaurants' own domains included.</summary>
public class IndexNowKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _path;
    private readonly string? _key;

    public IndexNowKeyMiddleware(RequestDelegate next, IOptions<IndexNowOptions> options)
    {
        _next = next;
        if (options.Value.Enabled)
        {
            _key = options.Value.Key;
            _path = "/" + _key + ".txt";
        }
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_path != null && HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals(_path, StringComparison.Ordinal))
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(_key!);
        }
        return _next(context);
    }
}
