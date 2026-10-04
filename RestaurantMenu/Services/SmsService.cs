using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantMenu.Services;

/// <summary>
/// SMS settings, bound from "Sms" (env vars Sms__TwilioAccountSid, ...). Optional:
/// without them bookings still work, with confirmations by email and on screen.
/// </summary>
public class SmsOptions
{
    /// <summary>auto (default: Twilio if set, else the webhook), twilio, webhook or none.</summary>
    public string? Provider { get; set; }

    public string? TwilioAccountSid { get; set; }
    public string? TwilioAuthToken { get; set; }
    /// <summary>The sending number (+355…), an alphanumeric sender ID, or a Messaging Service SID (MG…).</summary>
    public string? TwilioFrom { get; set; }
    public string TwilioEndpoint { get; set; } = "https://api.twilio.com";

    /// <summary>
    /// Any SMS gateway with an HTTP API (e.g. a local Albanian provider): the app POSTs
    /// JSON {"to": "+355…", "message": "…"} here, with "Authorization: Bearer {WebhookToken}"
    /// when a token is set. A 2xx answer counts as sent.
    /// </summary>
    public string? WebhookUrl { get; set; }
    public string? WebhookToken { get; set; }
}

/// <summary>Sends text messages through Twilio or a webhook gateway. Best effort: never throws to callers.</summary>
public class SmsService
{
    private readonly HttpClient _http;
    private readonly SmsOptions _o;
    private readonly ILogger<SmsService> _logger;

    public SmsService(HttpClient http, IOptions<SmsOptions> options, ILogger<SmsService> logger)
    {
        _http = http;
        _o = options.Value;
        _logger = logger;
    }

    private bool TwilioReady => !string.IsNullOrWhiteSpace(_o.TwilioAccountSid) && !string.IsNullOrWhiteSpace(_o.TwilioAuthToken) && !string.IsNullOrWhiteSpace(_o.TwilioFrom);
    private bool WebhookReady => Uri.TryCreate(_o.WebhookUrl, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http");

    /// <summary>"twilio", "webhook" or null.</summary>
    public string? Provider => (_o.Provider ?? "auto").Trim().ToLowerInvariant() switch
    {
        "none" or "off" => null,
        "twilio" => TwilioReady ? "twilio" : null,
        "webhook" => WebhookReady ? "webhook" : null,
        _ => TwilioReady ? "twilio" : WebhookReady ? "webhook" : null
    };

    public bool IsConfigured => Provider != null;
    public string? ProviderName => Provider switch { "twilio" => "Twilio", "webhook" => "SMS gateway (webhook)", _ => null };

    /// <summary>Returns whether the message was accepted by the provider.</summary>
    public async Task<bool> SendAsync(string to, string message, CancellationToken ct = default)
    {
        var provider = Provider;
        if (provider == null) return false;
        try
        {
            using var request = provider == "twilio" ? Twilio(to, message) : Webhook(to, message);
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("SMS sent through {Provider}", provider);
                return true;
            }
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("SMS through {Provider} refused ({Status}): {Body}", provider, (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "SMS through {Provider} failed", provider);
        }
        return false;
    }

    private HttpRequestMessage Twilio(string to, string message)
    {
        var sid = _o.TwilioAccountSid!.Trim();
        var from = _o.TwilioFrom!.Trim();
        var fields = new Dictionary<string, string> { ["To"] = to, ["Body"] = message };
        fields[from.StartsWith("MG", StringComparison.Ordinal) ? "MessagingServiceSid" : "From"] = from;
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_o.TwilioEndpoint.TrimEnd('/')}/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{_o.TwilioAuthToken!.Trim()}")));
        return request;
    }

    private HttpRequestMessage Webhook(string to, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _o.WebhookUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { to, message }), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_o.WebhookToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.WebhookToken.Trim());
        return request;
    }
}
