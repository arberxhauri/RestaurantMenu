using System.Net;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace RestaurantMenu.Services;

/// <summary>
/// Sends invite and notice emails when SMTP is configured. Without it (or when sending
/// fails) the caller shows the link once so the person inviting can pass it on.
/// Used for owner invites (Admin) and staff invites (Team).
/// </summary>
public class InviteMailer
{
    private readonly IEmailSender _email;
    private readonly SmtpOptions _smtp;
    private readonly ILogger<InviteMailer> _logger;

    public InviteMailer(IEmailSender email, IOptions<SmtpOptions> smtp, ILogger<InviteMailer> logger)
    {
        _email = email;
        _smtp = smtp.Value;
        _logger = logger;
    }

    public bool CanEmail => _smtp.IsConfigured;

    /// <summary>
    /// Emails a "set your password" link. Returns true when it was sent; false means the
    /// caller must show <paramref name="link"/> to be passed on by hand.
    /// </summary>
    /// <param name="introHtml">Opening sentence(s), already HTML-encoded.</param>
    public async Task<bool> SendSetPasswordAsync(string to, string name, string subject, string introHtml, string link)
    {
        if (!_smtp.IsConfigured) return false;
        try
        {
            await _email.SendEmailAsync(to, subject,
                $"<p>Hi {WebUtility.HtmlEncode(name)},</p><p>{introHtml}</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Set my password</a></p>" +
                "<p>The link works for 3 days.</p>");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invite email to {Email} could not be sent", to);
            return false;
        }
    }

    /// <summary>A plain notice (e.g. "you were added to a branch"). Best effort: false if not sent.</summary>
    public async Task<bool> SendNoticeAsync(string to, string subject, string html)
    {
        if (!_smtp.IsConfigured) return false;
        try
        {
            await _email.SendEmailAsync(to, subject, html);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notice email to {Email} could not be sent", to);
            return false;
        }
    }
}
