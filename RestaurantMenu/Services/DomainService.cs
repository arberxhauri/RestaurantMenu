using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

public record DomainResult(bool Ok, string Message);

/// <summary>
/// Restaurants' own domains: add (and, with a Render API key, register them with Render so
/// it issues the certificate), check that DNS really leads here, remove.
/// A domain goes live only once the app reaches itself through it: it asks
/// http://{domain}/.well-known/mqm-domain and expects the domain's own token back.
/// </summary>
public class DomainService
{
    public const string CheckPath = "/.well-known/mqm-domain";

    private readonly ApplicationDbContext _db;
    private readonly HttpClient _http;
    private readonly SiteOptions _o;
    private readonly SiteHosts _hosts;
    private readonly ILogger<DomainService> _logger;

    public DomainService(ApplicationDbContext db, HttpClient http, IOptions<SiteOptions> options, SiteHosts hosts, ILogger<DomainService> logger)
    {
        _db = db;
        _http = http;
        _o = options.Value;
        _hosts = hosts;
        _logger = logger;
    }

    public bool RenderApiConfigured => !string.IsNullOrWhiteSpace(_o.RenderApiKey) && !string.IsNullOrWhiteSpace(_o.RenderServiceId);

    public async Task<(Domain? Domain, string? Error)> AddAsync(int branchId, string? input, DateTime utcNow)
    {
        var host = SiteRules.NormalizeHost(input);
        if (host == null) return (null, "That isn't a domain name. Enter it like www.yourrestaurant.al, without https://.");
        if (_hosts.IsAppHost(host) || (_hosts.WildcardDomain is { } w && host.EndsWith("." + w)))
            return (null, "That address belongs to My Quick Menu itself.");
        if (await _db.Domains.IgnoreQueryFilters().AnyAsync(d => d.Host == host))
            return (null, $"{SiteRules.DisplayHost(host)} is already connected to a website here. If it's yours and you can't find it, contact us.");
        if (await _db.Domains.CountAsync(d => d.BranchId == branchId) >= SiteRules.MaxDomainsPerBranch)
            return (null, $"A website can have up to {SiteRules.MaxDomainsPerBranch} domains (e.g. yourrestaurant.al and www.yourrestaurant.al).");

        var d = new Domain
        {
            BranchId = branchId,
            Host = host,
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            CreatedUtc = utcNow
        };
        _db.Domains.Add(d);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return (null, $"{SiteRules.DisplayHost(host)} was just connected elsewhere.");
        }

        if (RenderApiConfigured)
        {
            var (id, error) = await RenderAddAsync(host);
            d.RenderId = id;
            if (error != null) d.LastError = Truncate($"Render: {error}");
            await _db.SaveChangesAsync();
        }
        _hosts.Invalidate();
        return (d, null);
    }

    public async Task RemoveAsync(Domain d)
    {
        if (RenderApiConfigured) await RenderSendAsync(HttpMethod.Delete, $"/custom-domains/{Uri.EscapeDataString(d.RenderId ?? d.Host)}");
        _db.Domains.Remove(d);
        await _db.SaveChangesAsync();
        _hosts.Invalidate();
    }

    /// <summary>Checks DNS and reachability, updates the domain, and says what to do next.</summary>
    public async Task<DomainResult> CheckAsync(Domain d, DateTime utcNow, CancellationToken ct = default)
    {
        var (ok, message) = await ProbeAsync(d, ct);
        d.LastCheckUtc = utcNow;
        if (ok)
        {
            if (!d.Verified) d.VerifiedUtc = utcNow;
            d.Verified = true;
            d.LastError = null;
            if (RenderApiConfigured) await RenderSendAsync(HttpMethod.Post, $"/custom-domains/{Uri.EscapeDataString(d.RenderId ?? d.Host)}/verify");
        }
        else
        {
            d.LastError = Truncate(message);
        }
        await _db.SaveChangesAsync(ct);
        _hosts.Invalidate();
        return new DomainResult(ok, ok ? $"{SiteRules.DisplayHost(d.Host)} is connected. Your website is live there." : message);
    }

    private async Task<(bool Ok, string Message)> ProbeAsync(Domain d, CancellationToken ct)
    {
        var shown = SiteRules.DisplayHost(d.Host);
        Uri target;
        if (_o.CheckEndpoint != null)
        {
            target = new Uri(_o.CheckEndpoint.TrimEnd('/') + CheckPath);
        }
        else
        {
            IPAddress[] addresses;
            try { addresses = await Dns.GetHostAddressesAsync(d.Host, AddressFamily.InterNetwork, ct); }
            catch (SocketException) { addresses = Array.Empty<IPAddress>(); }
            if (addresses.Length == 0)
                return (false, $"{shown} has no DNS record yet. Add the record shown below at your domain provider. Changes can take a few hours to show.");
            // Never let a check be pointed at internal addresses.
            if (addresses.Any(IsPrivate))
                return (false, $"{shown} points to a private address ({string.Join(", ", addresses.Select(a => a.ToString()))}). Point it to My Quick Menu as shown below.");
            target = new Uri($"http://{d.Host}{CheckPath}");
        }

        for (var hop = 0; hop < 2; hop++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                request.Headers.Host = d.Host;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                // Render sends http to https on the same host; follow that once, nowhere else.
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } loc
                    && loc.IsAbsoluteUri && loc.Scheme == "https" && string.Equals(loc.Host, d.Host, StringComparison.OrdinalIgnoreCase) && _o.CheckEndpoint == null)
                {
                    target = loc;
                    continue;
                }
                var body = await ReadLimitedAsync(response, cts.Token);
                if (response.IsSuccessStatusCode && body.Trim() == d.Token) return (true, "");
                return (false, $"{shown} points to a different server, not My Quick Menu yet. Set the DNS record shown below (and remove other A/AAAA records for it).");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (false, $"{shown} didn't answer ({(ex is TaskCanceledException ? "timed out" : "couldn't connect")}). If you just changed DNS, wait a little and check again.");
            }
        }
        return (false, $"{shown} redirects somewhere else. Remove any forwarding set up at your domain provider.");
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static bool IsPrivate(IPAddress a)
    {
        if (IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] : s;

    // ---------------------------------------------------------------- Render API (optional)

    private async Task<(string? Id, string? Error)> RenderAddAsync(string host)
    {
        var (ok, body) = await RenderSendAsync(HttpMethod.Post, "/custom-domains", JsonSerializer.Serialize(new { name = host }));
        if (!ok) return (null, body);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            var mine = items.FirstOrDefault(i => i.TryGetProperty("name", out var n) && n.GetString() == host);
            return (mine.ValueKind == JsonValueKind.Object && mine.TryGetProperty("id", out var id) ? id.GetString() : null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private async Task<(bool Ok, string Body)> RenderSendAsync(HttpMethod method, string path, string? json = null)
    {
        if (!RenderApiConfigured) return (false, "not configured");
        try
        {
            using var request = new HttpRequestMessage(method, $"{_o.RenderApiBase.TrimEnd('/')}/services/{Uri.EscapeDataString(_o.RenderServiceId!)}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.RenderApiKey!.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Render API {Method} {Path} answered {Status}", method, path, (int)response.StatusCode);
                return (false, $"{(int)response.StatusCode} {(body.Length > 200 ? body[..200] : body)}");
            }
            return (true, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Render API {Method} {Path} failed", method, path);
            return (false, "Render couldn't be reached");
        }
    }
}

/// <summary>Re-checks domains that aren't connected yet every 10 minutes for 7 days after they were added.</summary>
public class DomainCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DomainCheckService> _logger;

    public DomainCheckService(IServiceScopeFactory scopes, ILogger<DomainCheckService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var domains = scope.ServiceProvider.GetRequiredService<DomainService>();
                    var since = DateTime.UtcNow.AddDays(-7);
                    foreach (var d in await db.Domains.Where(d => !d.Verified && d.CreatedUtc > since).Take(50).ToListAsync(stoppingToken))
                    {
                        var r = await domains.CheckAsync(d, DateTime.UtcNow, stoppingToken);
                        if (r.Ok) _logger.LogInformation("Domain {Host} connected", d.Host);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Domain checks failed; retrying later");
                }
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
