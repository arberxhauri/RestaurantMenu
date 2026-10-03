using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// The two kinds of emailed link, each with its own lifetime and its own signing key
/// purpose, so one can never be used as the other:
/// invites (set a first password, 3 days) and password resets (2 hours).
/// Both stop working once used: setting a password changes the account's security stamp,
/// which every token is tied to.
/// </summary>
public static class AccountTokens
{
    public const string Invite = "Invite";
    public const string PasswordReset = "PasswordReset";

    public static readonly TimeSpan InviteLifespan = TimeSpan.FromDays(3);
    public static readonly TimeSpan ResetLifespan = TimeSpan.FromHours(2);

    public const string InviteLifespanText = "3 days";
    public const string ResetLifespanText = "2 hours";
}

public class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions()
    {
        Name = "InviteTokenProvider";
        TokenLifespan = AccountTokens.InviteLifespan;
    }
}

public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = "PasswordResetTokenProvider";
        TokenLifespan = AccountTokens.ResetLifespan;
    }
}

public class InviteTokenProvider : DataProtectorTokenProvider<ApplicationUser>
{
    public InviteTokenProvider(IDataProtectionProvider protection, IOptions<InviteTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<ApplicationUser>> logger) : base(protection, options, logger) { }
}

public class PasswordResetTokenProvider : DataProtectorTokenProvider<ApplicationUser>
{
    public PasswordResetTokenProvider(IDataProtectionProvider protection, IOptions<PasswordResetTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<ApplicationUser>> logger) : base(protection, options, logger) { }
}
