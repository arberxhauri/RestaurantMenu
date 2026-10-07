using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RestaurantMenu.Services;

/// <summary>
/// Paddle settings, bound from "Billing:Paddle" (env vars Billing__Paddle__ApiKey, ...). Secrets,
/// so environment variables rather than the database. Card payments are offered when these are
/// set and "Card payments" is ticked in Admin → Plans &amp; prices.
/// </summary>
public class PaddleOptions
{
    /// <summary>sandbox (default) or production.</summary>
    public string Environment { get; set; } = "sandbox";
    /// <summary>Server-side API key (Paddle → Developer tools → Authentication).</summary>
    public string? ApiKey { get; set; }
    /// <summary>The webhook destination's secret key (Paddle → Developer tools → Notifications).</summary>
    public string? WebhookSecret { get; set; }
    /// <summary>Client-side token for Paddle.js on the checkout page (public).</summary>
    public string? ClientToken { get; set; }
    /// <summary>Tests only: another API address (a local mock).</summary>
    public string? ApiBase { get; set; }

    public bool IsSandbox => !string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);
    /// <summary>"sandbox" or "production": which Paddle account stored ids belong to.</summary>
    public string EnvironmentKey => IsSandbox ? "sandbox" : "production";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(WebhookSecret) && !string.IsNullOrWhiteSpace(ClientToken);
    public string BaseUrl => (ApiBase ?? (IsSandbox ? "https://sandbox-api.paddle.com" : "https://api.paddle.com")).TrimEnd('/');
}

public class PaddleException : Exception
{
    public HttpStatusCode Status { get; }
    public string? Code { get; }
    public PaddleException(HttpStatusCode status, string? code, string message) : base(message) { Status = status; Code = code; }
}

/// <summary>
/// The Paddle Billing API calls the app makes. Every call is server to server with the API key;
/// the browser only ever sees the client token on the checkout page.
/// </summary>
public class PaddleClient
{
    private readonly HttpClient _http;
    private readonly PaddleOptions _o;

    public PaddleClient(HttpClient http, IOptions<PaddleOptions> options)
    {
        _http = http;
        _o = options.Value;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public bool IsConfigured => _o.IsConfigured;
    public string EnvironmentKey => _o.EnvironmentKey;

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, _o.BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.ApiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        var json = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
        if (!response.IsSuccessStatusCode)
        {
            var error = json["error"];
            throw new PaddleException(response.StatusCode, error?["code"]?.GetValue<string>(),
                $"Paddle {method} {path}: {(int)response.StatusCode} {error?["detail"]?.GetValue<string>() ?? text}");
        }
        return json["data"] ?? json;
    }

    private static string Id(JsonNode data) => data["id"]!.GetValue<string>();

    /// <summary>The customer with this email, created if Paddle doesn't know it yet.</summary>
    public async Task<string> CustomerAsync(string email, string name, CancellationToken ct = default)
    {
        var found = await SendAsync(HttpMethod.Get, "/customers?email=" + Uri.EscapeDataString(email), ct: ct);
        if (found is JsonArray { Count: > 0 } list) return Id(list[0]!);
        return Id(await SendAsync(HttpMethod.Post, "/customers", new { email, name }, ct));
    }

    public async Task<string> CreateProductAsync(string name, CancellationToken ct = default) =>
        Id(await SendAsync(HttpMethod.Post, "/products", new { name, tax_category = "saas" }, ct));

    /// <summary>A recurring price: cents per unit, each month or year, up to 100 units (branches).</summary>
    public async Task<string> CreatePriceAsync(string productId, string description, int cents, string currency, bool yearly, CancellationToken ct = default) =>
        Id(await SendAsync(HttpMethod.Post, "/prices", new
        {
            product_id = productId,
            description,
            unit_price = new { amount = cents.ToString(System.Globalization.CultureInfo.InvariantCulture), currency_code = currency },
            billing_cycle = new { interval = yearly ? "year" : "month", frequency = 1 },
            quantity = new { minimum = 1, maximum = 100 },
            tax_mode = "account_setting"
        }, ct));

    public Task ArchivePriceAsync(string priceId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/prices/{priceId}", new { status = "archived" }, ct);

    /// <summary>A transaction to pay by card; its id opens Paddle's checkout on /billing/pay.</summary>
    public async Task<string> CreateTransactionAsync(string customerId, IEnumerable<(string PriceId, int Quantity)> items, string ownerId, CancellationToken ct = default) =>
        Id(await SendAsync(HttpMethod.Post, "/transactions", new
        {
            customer_id = customerId,
            items = items.Select(i => new { price_id = i.PriceId, quantity = i.Quantity }).ToArray(),
            custom_data = new { owner_id = ownerId },
            collection_mode = "automatic"
        }, ct));

    /// <summary>New items for a subscription, charged or credited for the rest of the period now.</summary>
    public Task UpdateItemsAsync(string subscriptionId, IEnumerable<(string PriceId, int Quantity)> items, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/subscriptions/{subscriptionId}", new
        {
            items = items.Select(i => new { price_id = i.PriceId, quantity = i.Quantity }).ToArray(),
            proration_billing_mode = "prorated_immediately"
        }, ct);

    public Task CancelAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/subscriptions/{subscriptionId}/cancel", new { effective_from = "next_billing_period" }, ct);

    /// <summary>Takes back a scheduled cancellation.</summary>
    public Task ResumeAsync(string subscriptionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/subscriptions/{subscriptionId}", new Dictionary<string, object?> { ["scheduled_change"] = null }, ct);

    /// <summary>A short-lived link to Paddle's customer portal (change card, receipts).</summary>
    public async Task<string> PortalUrlAsync(string customerId, string? subscriptionId, CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Post, $"/customers/{customerId}/portal-sessions",
            new { subscription_ids = subscriptionId == null ? Array.Empty<string>() : new[] { subscriptionId } }, ct);
        return data["urls"]!["general"]!["overview"]!.GetValue<string>();
    }

    /// <summary>A temporary link to the PDF invoice Paddle issued for a transaction.</summary>
    public async Task<string> InvoiceUrlAsync(string transactionId, CancellationToken ct = default) =>
        (await SendAsync(HttpMethod.Get, $"/transactions/{transactionId}/invoice", ct: ct))["url"]!.GetValue<string>();
}
