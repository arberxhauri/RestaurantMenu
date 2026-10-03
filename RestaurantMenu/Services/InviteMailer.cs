using RestaurantMenu.Helpers;

namespace RestaurantMenu.Services;

/// <summary>
/// The account emails: invites (owner from Admin, staff from Team), "you were added"
/// notices, and password reset links. Invites and notices are sent right away so the
/// person inviting learns whether it worked; without email (or when sending fails) the
/// caller shows the link once to pass on by hand. Reset links go through the background
/// queue (see AccountController.ForgotPassword).
/// All text passed in is plain text; the template encodes it.
/// </summary>
public class InviteMailer
{
    private readonly EmailService _email;
    private readonly EmailQueue _queue;
    private readonly ILogger<InviteMailer> _logger;

    public InviteMailer(EmailService email, EmailQueue queue, ILogger<InviteMailer> logger)
    {
        _email = email;
        _queue = queue;
        _logger = logger;
    }

    public bool CanEmail => _email.IsConfigured;

    /// <summary>
    /// Emails a "set your password" link. Returns true when it was sent; false means the
    /// caller must show <paramref name="link"/> to be passed on by hand.
    /// </summary>
    public async Task<bool> SendSetPasswordAsync(string to, string name, string subject, string intro, string link)
    {
        if (!_email.IsConfigured) return false;
        var (html, text) = EmailTemplate.Render(name, intro, new[] { intro },
            "Set my password", link,
            $"The link works once, for {AccountTokens.InviteLifespanText}. After that, ask for a new one.");
        return await TrySendAsync(new EmailMessage(to, name, subject, html, text));
    }

    /// <summary>A notice (e.g. "you were added to a branch") with an optional button. Best effort: false if not sent.</summary>
    public async Task<bool> SendNoticeAsync(string to, string name, string subject, string message, string? buttonText = null, string? link = null)
    {
        if (!_email.IsConfigured) return false;
        var (html, text) = EmailTemplate.Render(name, message, new[] { message }, buttonText, link);
        return await TrySendAsync(new EmailMessage(to, name, subject, html, text));
    }

    /// <summary>Queues a password reset link; returns at once, whatever happens (see EmailQueue).</summary>
    public void QueuePasswordReset(string to, string name, string link)
    {
        var (html, text) = EmailTemplate.Render(name,
            "Someone asked to reset your My Quick Menu password.",
            new[]
            {
                "Someone (hopefully you) asked to reset the password for your My Quick Menu account.",
                "Choose a new password with the button below. Until you do, your current password keeps working."
            },
            "Choose a new password", link,
            $"The link works once, for {AccountTokens.ResetLifespanText}. If you didn't ask for this, ignore this email; your password stays the same.");
        _queue.Enqueue(new EmailMessage(to, name, "Reset your My Quick Menu password", html, text));
    }

    private async Task<bool> TrySendAsync(EmailMessage message)
    {
        try
        {
            await _email.SendAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email \"{Subject}\" could not be sent", message.Subject);
            return false;
        }
    }
}
