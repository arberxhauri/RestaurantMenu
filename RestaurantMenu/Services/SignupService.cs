using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Signup settings, bound from "Signup" (env vars Signup__NotifyEmail, ...). Enabled and
/// RequireApproval only seed the plan settings on the very first start; after that they are
/// switched in Admin → Plans &amp; prices (PlanSettings).
/// </summary>
public class SignupOptions
{
    /// <summary>First-start seed for PlanSettings.SignupEnabled.</summary>
    public bool Enabled { get; set; }
    /// <summary>First-start seed for PlanSettings.SignupRequireApproval.</summary>
    public bool RequireApproval { get; set; } = true;
    /// <summary>Where "someone is waiting for approval" emails go (optional).</summary>
    public string? NotifyEmail { get; set; }
    /// <summary>Unconfirmed signups are deleted after this many days.</summary>
    public int UnconfirmedDays { get; set; } = 7;
}

/// <summary>What a signup form brings in, already cleaned.</summary>
public record SignupInput(string FullName, string Email, string Password, string Restaurant, string Country, string Language);

public enum SignupOutcome { Created, EmailTaken, EmailRemoved }

public enum ConfirmOutcome { Invalid, AlreadyConfirmed, WaitingForApproval, Opened }

/// <summary>
/// Self-serve accounts, start to finish: create the owner (unconfirmed) with a trial of the
/// modules they picked, email a confirmation link, confirm it, and open the account, either at
/// once or after the admin's approval (Signup__RequireApproval, or a second trial on the same
/// company domain). The trial clock starts when the account opens. Signup is open only when it
/// is switched on in Admin → Plans &amp; prices and email works (<see cref="IsOpenAsync"/>), since
/// the link is the only way in.
/// </summary>
public class SignupService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SubscriptionService _subscriptions;
    private readonly EmailService _email;
    private readonly SeoService _seo;
    private readonly SignupOptions _options;
    private readonly PlanSettingsService _settings;
    private readonly ILogger<SignupService> _logger;

    public SignupService(ApplicationDbContext db, UserManager<ApplicationUser> users, SubscriptionService subscriptions,
        EmailService email, SeoService seo, IOptions<SignupOptions> options, PlanSettingsService settings, ILogger<SignupService> logger)
    {
        _db = db;
        _users = users;
        _subscriptions = subscriptions;
        _email = email;
        _seo = seo;
        _options = options.Value;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Switched on in the plan settings, and email works.</summary>
    public async Task<bool> IsOpenAsync() => _email.IsConfigured && (await _settings.GetAsync()).SignupEnabled;
    public bool EmailWorks => _email.IsConfigured;
    public async Task<int> TrialDaysAsync() => Math.Max(1, (await _settings.GetAsync()).TrialDays);

    /// <summary>
    /// Creates the owner: unconfirmed, role OWNER, a trial of <paramref name="plan"/> that hasn't
    /// started. An email that already has an account (removed ones included: their email stays
    /// theirs) is refused, never a database error. Doesn't send the confirmation email.
    /// </summary>
    public async Task<(SignupOutcome Outcome, ApplicationUser? User, IEnumerable<string> Errors)> CreateAsync(
        SignupInput input, PlanSelection plan, DateTime utcNow)
    {
        var normalized = _users.NormalizeEmail(input.Email);
        var existing = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized || u.NormalizedUserName == normalized);
        if (existing != null)
        {
            if (existing.IsDeleted) _logger.LogInformation("Signup: refused {Email}, which belonged to a removed account", input.Email);
            return (existing.IsDeleted ? SignupOutcome.EmailRemoved : SignupOutcome.EmailTaken, null, Array.Empty<string>());
        }

        var user = new ApplicationUser
        {
            UserName = input.Email,
            Email = input.Email,
            FullName = input.FullName,
            NIPT = "",
            NumberOfBranches = plan.Branches,
            EmailConfirmed = false,
            SignupSource = SignupSource.SelfServe,
            Country = input.Country,
            Language = input.Language,
            TermsVersion = TermsInfo.Version,
            TermsAcceptedUtc = utcNow,
            SignupRestaurantName = input.Restaurant
        };

        var strategy = _db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                var created = await _users.CreateAsync(user, input.Password);
                if (!created.Succeeded) return (SignupOutcome.EmailTaken, (ApplicationUser?)null, created.Errors.Select(e => e.Description));
                await _users.AddToRoleAsync(user, "OWNER");
                await _subscriptions.AddTrialAsync(user, plan, utcNow);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (SignupOutcome.Created, user, Enumerable.Empty<string>());
            });
        }
        catch (DbUpdateException)
        {
            // The same email signed up a moment ago (unique index).
            return (SignupOutcome.EmailTaken, null, Array.Empty<string>());
        }
    }

    /// <summary>Emails the confirmation link (3 days). False when it couldn't be sent.</summary>
    public async Task<bool> SendConfirmationAsync(ApplicationUser user)
    {
        if (!_email.IsConfigured || user.EmailConfirmed) return false;
        var w = SignupText.For(user.Language);
        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var link = _seo.Url($"/signup/confirm?u={Uri.EscapeDataString(user.Id)}&t={Uri.EscapeDataString(token)}&lang={user.Language}");
        var (html, text) = EmailTemplate.Render(user.FullName, w.VerifyPreheader,
            new[] { string.Format(w.VerifyParagraph, user.SignupRestaurantName ?? "My Quick Menu") },
            w.VerifyEmailButton, link, w.VerifyFooter);
        try
        {
            await _email.SendAsync(new EmailMessage(user.Email!, user.FullName, w.VerifySubject, html, text));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signup: confirmation email to {Email} could not be sent", user.Email);
            return false;
        }
    }

    /// <summary>
    /// The confirmation link: confirms the email, then opens the account (trial starts) unless it
    /// must wait for the admin. <see cref="ConfirmOutcome.Opened"/> means the caller may sign them in.
    /// </summary>
    public async Task<(ConfirmOutcome Outcome, ApplicationUser? User)> ConfirmAsync(string? userId, string? token, DateTime utcNow)
    {
        var user = string.IsNullOrEmpty(userId) ? null : await _users.FindByIdAsync(userId);
        if (user == null || user.SignupSource != SignupSource.SelfServe || string.IsNullOrEmpty(token)) return (ConfirmOutcome.Invalid, null);
        if (user.EmailConfirmed) return (user.ApprovedUtc == null ? ConfirmOutcome.WaitingForApproval : ConfirmOutcome.AlreadyConfirmed, user);

        var result = await _users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded) return (ConfirmOutcome.Invalid, null);

        var note = await ApprovalNoteAsync(user);
        if ((await _settings.GetAsync()).SignupRequireApproval || note != null)
        {
            user.ApprovalNote = note;
            await _users.UpdateAsync(user);
            await NotifyAdminAsync(user);
            _logger.LogInformation("Signup: {Email} confirmed, waiting for approval{Note}", user.Email, note == null ? "" : $" ({note})");
            return (ConfirmOutcome.WaitingForApproval, user);
        }

        await OpenAsync(user, actorId: null, utcNow);
        _logger.LogInformation("Signup: {Email} confirmed, account open", user.Email);
        return (ConfirmOutcome.Opened, user);
    }

    /// <summary>The admin lets a waiting account in: it opens, the trial starts, and they get an email.</summary>
    public async Task<bool> ApproveAsync(string userId, string? adminId, DateTime utcNow)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || !user.AwaitingApproval) return false;
        await OpenAsync(user, adminId, utcNow);

        var w = SignupText.For(user.Language);
        if (_email.IsConfigured)
        {
            var (html, text) = EmailTemplate.Render(user.FullName, w.ApprovedSubject,
                new[] { string.Format(w.ApprovedText, await TrialDaysAsync()) }, w.ApprovedButton, _seo.Url("/Account/Login"));
            try { await _email.SendAsync(new EmailMessage(user.Email!, user.FullName, w.ApprovedSubject, html, text)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Signup: approval email to {Email} could not be sent", user.Email); }
        }
        return true;
    }

    private async Task OpenAsync(ApplicationUser user, string? actorId, DateTime utcNow)
    {
        user.ApprovedUtc = utcNow;
        await _users.UpdateAsync(user);
        await _subscriptions.StartTrialAsync(user.Id, actorId, utcNow);
    }

    /// <summary>
    /// Why this account should be looked at even without approval mode: another trial was
    /// already started from the same company domain (webmail doesn't count). Null when it's fine.
    /// </summary>
    private async Task<string?> ApprovalNoteAsync(ApplicationUser user)
    {
        var domain = SignupRules.Domain(user.Email);
        if (!SignupRules.IsCompanyDomain(domain)) return null;
        var suffix = "@" + domain;
        var other = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id != user.Id && u.SignupSource == SignupSource.SelfServe && u.EmailConfirmed && u.NormalizedEmail!.EndsWith(suffix.ToUpperInvariant()))
            .Select(u => u.Email).FirstOrDefaultAsync();
        return other == null ? null : $"Another trial already uses {domain} ({other}).";
    }

    private async Task NotifyAdminAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(_options.NotifyEmail) || !_email.IsConfigured) return;
        var text = $"{user.FullName} ({user.Email}) signed up for \"{user.SignupRestaurantName}\" and confirmed their email." +
                   (user.ApprovalNote == null ? "" : " " + user.ApprovalNote) + " Approve or decline them in the admin.";
        var (html, plain) = EmailTemplate.Render("there", "A new restaurant is waiting for approval", new[] { text }, "Open the admin", _seo.Url("/admin"));
        try { await _email.SendAsync(new EmailMessage(_options.NotifyEmail.Trim(), null, "New signup waiting for approval", html, plain)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Signup: approval notice could not be sent"); }
    }

    /// <summary>
    /// Deletes self-serve accounts that never confirmed their email within
    /// Signup__UnconfirmedDays (7), with their unstarted trial, so abandoned or fake signups
    /// don't keep an email address or pile up. Run daily.
    /// </summary>
    public static async Task<int> DeleteUnconfirmedAsync(ApplicationDbContext db, int days, DateTime utcNow, CancellationToken ct)
    {
        // Accounts have no creation date of their own: the terms were accepted at signup.
        var cutoff = utcNow.AddDays(-Math.Max(1, days));
        var ids = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SignupSource == SignupSource.SelfServe && !u.EmailConfirmed && u.TermsAcceptedUtc < cutoff)
            .Select(u => u.Id).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        var subs = db.Subscriptions.IgnoreQueryFilters().Where(s => ids.Contains(s.OwnerId));
        await db.SubscriptionAudits.Where(a => subs.Select(s => s.Id).Contains(a.SubscriptionId)).ExecuteDeleteAsync(ct);
        await subs.ExecuteDeleteAsync(ct); // items go by cascade
        await db.UserRoles.Where(r => ids.Contains(r.UserId)).ExecuteDeleteAsync(ct);
        return await db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync(ct);
    }
}
