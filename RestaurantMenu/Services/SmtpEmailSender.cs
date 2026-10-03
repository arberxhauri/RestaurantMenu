using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace RestaurantMenu.Services;

/// <summary>SMTP settings, bound from the "Smtp" section (env vars Smtp__Host, Smtp__Port, ...).</summary>
public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public bool EnableSsl { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// Sends mail through any SMTP provider (Resend, Postmark, SendGrid, Gmail...).
/// When SMTP is not configured it throws, so callers check <see cref="SmtpOptions.IsConfigured"/> first
/// and fall back to showing the link on screen.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options) => _options = options.Value;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("SMTP is not configured.");

        using var message = new MailMessage(_options.From!, email, subject, htmlMessage) { IsBodyHtml = true };
        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl };
        if (!string.IsNullOrEmpty(_options.User))
            client.Credentials = new NetworkCredential(_options.User, _options.Password);

        await client.SendMailAsync(message);
    }
}
