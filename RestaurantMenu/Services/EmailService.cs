using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace RestaurantMenu.Services;

/// <summary>
/// Email settings, bound from the "Email" section (env vars Email__From, Email__ResendApiKey, ...).
/// Set a From address and one provider's key; the provider is picked automatically.
/// </summary>
public class EmailOptions
{
    /// <summary>auto (default: the first configured of Resend, Brevo, SMTP), resend, brevo, smtp or none.</summary>
    public string? Provider { get; set; }

    /// <summary>Sender address on your verified domain, e.g. hello@myquickmenu.al. Falls back to Smtp__From.</summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "My Quick Menu";
    /// <summary>Where replies go (optional), e.g. your support inbox.</summary>
    public string? ReplyTo { get; set; }

    public string? ResendApiKey { get; set; }
    public string? BrevoApiKey { get; set; }

    // Overridable for tests (a local mock server); never needed in production.
    public string ResendEndpoint { get; set; } = "https://api.resend.com/emails";
    public string BrevoEndpoint { get; set; } = "https://api.brevo.com/v3/smtp/email";
}

/// <summary>
/// One email. <see cref="Text"/> is the plain-text part, which spam filters and some readers want.
/// <see cref="Attachments"/>: files to attach (an invoice PDF).
/// </summary>
public record EmailMessage(string To, string? ToName, string Subject, string Html, string Text,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>A file attached to an email.</summary>
public record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Who emails come from, resolved once from the settings.</summary>
public record EmailSender(string Address, string Name, string? ReplyTo)
{
    /// <summary>"My Quick Menu &lt;hello@example.com&gt;".</summary>
    public string Formatted => Name.IndexOfAny(new[] { ',', ';', '<', '>', '"', '@', ':', '(', ')', '[', ']', '\\' }) < 0
        ? $"{Name} <{Address}>"
        : $"\"{Name.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" <{Address}>";
}

/// <summary>A way of delivering email: an HTTPS API (Resend, Brevo) or SMTP.</summary>
public interface IEmailTransport
{
    /// <summary>Lower-case id used in Email__Provider: resend, brevo, smtp.</summary>
    string Id { get; }
    /// <summary>For people: "Resend", "Brevo", "SMTP (smtp.example.com)".</summary>
    string DisplayName { get; }
    bool IsConfigured { get; }
    /// <summary>Sends or throws <see cref="EmailSendException"/>.</summary>
    Task SendAsync(EmailMessage message, EmailSender from, CancellationToken ct = default);
}

public class EmailSendException : Exception
{
    public EmailSendException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The app's email sender. Picks the provider from the settings and sends through it.
/// Render's free plan blocks outgoing SMTP ports, so Resend or Brevo (HTTPS) are the
/// providers that work there; SMTP stays for hosts that allow it.
/// Callers check <see cref="IsConfigured"/> and, without email, show links on screen instead.
/// </summary>
public class EmailService : IEmailSender
{
    private static readonly string[] AutoOrder = { "resend", "brevo", "smtp" };

    private readonly IEmailTransport? _transport;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IEnumerable<IEmailTransport> transports, IOptions<EmailOptions> options,
        IOptions<SmtpOptions> smtp, ILogger<EmailService> logger)
    {
        _logger = logger;
        var o = options.Value;
        var all = transports.ToList();
        var wanted = (o.Provider ?? "auto").Trim().ToLowerInvariant();

        _transport = wanted switch
        {
            "none" or "off" => null,
            "" or "auto" => AutoOrder.Select(id => all.FirstOrDefault(t => t.Id == id)).FirstOrDefault(t => t?.IsConfigured == true),
            _ => all.FirstOrDefault(t => t.Id == wanted && t.IsConfigured)
        };

        Sender = ResolveSender(o, smtp.Value);
        if (Sender == null) _transport = null; // a provider without a From address can't send

        Problem = wanted is "none" or "off" ? "Email is switched off (Email__Provider=none)."
            : _transport != null ? null
            : Sender == null && all.Any(t => t.IsConfigured) ? "A provider key is set but Email__From is missing."
            : wanted is not ("" or "auto") ? $"Email__Provider is \"{wanted}\" but that provider's settings are missing."
            : "No email provider is set up.";
    }

    private static EmailSender? ResolveSender(EmailOptions o, SmtpOptions smtp)
    {
        var raw = !string.IsNullOrWhiteSpace(o.From) ? o.From : smtp.From;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            // Accepts "hello@x.com" or "Name <hello@x.com>"; a name in the address wins over FromName.
            var address = new MailAddress(raw.Trim());
            var name = string.IsNullOrWhiteSpace(address.DisplayName) ? o.FromName : address.DisplayName;
            return new EmailSender(address.Address, name, ParseOrNull(o.ReplyTo));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ParseOrNull(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        try { return new MailAddress(address.Trim()).Address; }
        catch (FormatException) { return null; } // a broken reply-to shouldn't stop email
    }

    public bool IsConfigured => _transport != null;
    public string? ProviderName => _transport?.DisplayName;
    public EmailSender? Sender { get; }
    /// <summary>Why email is off, in words for the admin page; null when it works.</summary>
    public string? Problem { get; }

    /// <summary>Sends or throws. Check <see cref="IsConfigured"/> first.</summary>
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (_transport == null || Sender == null) throw new EmailSendException("Email is not configured.");
        await _transport.SendAsync(message, Sender, ct);
        _logger.LogInformation("Email \"{Subject}\" sent through {Provider}", message.Subject, _transport.Id);
    }

    /// <summary>For ASP.NET's IEmailSender: HTML only, so the text part is derived from it.</summary>
    public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
        SendAsync(new EmailMessage(email, null, subject, htmlMessage, Helpers.EmailTemplate.HtmlToText(htmlMessage)));
}
