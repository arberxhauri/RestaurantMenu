using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantMenu.Services;

/// <summary>
/// Shared HTTPS sending: JSON body with a Content-Length, one retry on 429/5xx/timeouts
/// (with the same idempotency key, so a retry never sends twice), and errors that say
/// what the provider answered without ever including the API key.
/// </summary>
public abstract class HttpEmailTransport : IEmailTransport
{
    private readonly HttpClient _http;
    protected HttpEmailTransport(HttpClient http) => _http = http;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract bool IsConfigured { get; }

    protected abstract HttpRequestMessage Build(EmailMessage message, EmailSender from, string idempotencyKey);

    public async Task SendAsync(EmailMessage message, EmailSender from, CancellationToken ct = default)
    {
        var key = Guid.NewGuid().ToString("N");
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(Build(message, from, key), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt < 2) { await Task.Delay(1000, ct); continue; }
                throw new EmailSendException($"{DisplayName} could not be reached.", ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode) return;
                var status = (int)response.StatusCode;
                if (attempt < 2 && (status == 429 || status >= 500)) { await Task.Delay(1500, ct); continue; }

                var body = await response.Content.ReadAsStringAsync(ct);
                if (body.Length > 300) body = body[..300];
                throw new EmailSendException($"{DisplayName} refused the email ({status}): {body}");
            }
        }
    }

    protected static HttpContent Json(object body)
    {
        // StringContent sets Content-Length; some APIs and proxies reject chunked bodies.
        return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }
}

/// <summary>Resend (resend.com). Free: 3,000 emails a month, 100 a day, from a verified domain.</summary>
public class ResendEmailTransport : HttpEmailTransport
{
    private readonly EmailOptions _o;
    public ResendEmailTransport(HttpClient http, IOptions<EmailOptions> options) : base(http) => _o = options.Value;

    public override string Id => "resend";
    public override string DisplayName => "Resend";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_o.ResendApiKey);

    protected override HttpRequestMessage Build(EmailMessage m, EmailSender from, string idempotencyKey)
    {
        var body = new Dictionary<string, object>
        {
            ["from"] = from.Formatted,
            ["to"] = new[] { m.To },
            ["subject"] = m.Subject,
            ["html"] = m.Html,
            ["text"] = m.Text
        };
        if (from.ReplyTo != null) body["reply_to"] = from.ReplyTo;

        var request = new HttpRequestMessage(HttpMethod.Post, _o.ResendEndpoint) { Content = Json(body) };
        request.Headers.Authorization = new("Bearer", _o.ResendApiKey!.Trim());
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}

/// <summary>Brevo (brevo.com). Free: 300 emails a day, from a verified sender or domain.</summary>
public class BrevoEmailTransport : HttpEmailTransport
{
    private readonly EmailOptions _o;
    public BrevoEmailTransport(HttpClient http, IOptions<EmailOptions> options) : base(http) => _o = options.Value;

    public override string Id => "brevo";
    public override string DisplayName => "Brevo";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_o.BrevoApiKey);

    protected override HttpRequestMessage Build(EmailMessage m, EmailSender from, string idempotencyKey)
    {
        var to = new Dictionary<string, string> { ["email"] = m.To };
        if (!string.IsNullOrWhiteSpace(m.ToName)) to["name"] = m.ToName;
        var body = new Dictionary<string, object>
        {
            ["sender"] = new { name = from.Name, email = from.Address },
            ["to"] = new[] { to },
            ["subject"] = m.Subject,
            ["htmlContent"] = m.Html,
            ["textContent"] = m.Text
        };
        if (from.ReplyTo != null) body["replyTo"] = new { email = from.ReplyTo };

        var request = new HttpRequestMessage(HttpMethod.Post, _o.BrevoEndpoint) { Content = Json(body) };
        request.Headers.Add("api-key", _o.BrevoApiKey!.Trim());
        request.Headers.Add("Accept", "application/json");
        return request;
    }
}

/// <summary>SMTP settings, bound from the "Smtp" section (env vars Smtp__Host, Smtp__Port, ...).</summary>
public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public bool EnableSsl { get; set; } = true;
}

/// <summary>
/// Any SMTP server. Not usable on Render's free plan (outgoing SMTP ports are blocked
/// there); use Resend or Brevo instead.
/// </summary>
public class SmtpEmailTransport : IEmailTransport
{
    private readonly SmtpOptions _o;
    public SmtpEmailTransport(IOptions<SmtpOptions> options) => _o = options.Value;

    public string Id => "smtp";
    public string DisplayName => $"SMTP ({_o.Host})";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.Host);

    public async Task SendAsync(EmailMessage m, EmailSender from, CancellationToken ct = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(from.Address, from.Name),
            Subject = m.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        message.To.Add(string.IsNullOrWhiteSpace(m.ToName) ? new MailAddress(m.To) : new MailAddress(m.To, m.ToName));
        if (from.ReplyTo != null) message.ReplyToList.Add(from.ReplyTo);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(m.Text, Encoding.UTF8, "text/plain"));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(m.Html, Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(_o.Host, _o.Port) { EnableSsl = _o.EnableSsl, Timeout = 20000 };
        if (!string.IsNullOrEmpty(_o.User)) client.Credentials = new NetworkCredential(_o.User, _o.Password);
        try
        {
            await client.SendMailAsync(message, ct);
        }
        catch (SmtpException ex)
        {
            throw new EmailSendException($"The SMTP server refused the email: {ex.Message}", ex);
        }
    }
}
