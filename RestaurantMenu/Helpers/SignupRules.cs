using System.ComponentModel.DataAnnotations;

namespace RestaurantMenu.Helpers;

/// <summary>The Terms of use version a signup accepts (the "Last updated" date on /home/terms).</summary>
public static class TermsInfo
{
    public const string Version = "2026-10-05";
}

/// <summary>Signup checks that need no database (tested in SignupRulesTests).</summary>
public static class SignupRules
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;
    public const int MinRestaurantLength = 2;
    public const int MaxRestaurantLength = 80;

    /// <summary>Webmail domains: a second trial from the same one says nothing about the business.</summary>
    private static readonly HashSet<string> FreeMail = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "yahoo.com", "yahoo.it", "yahoo.co.uk", "ymail.com", "hotmail.com", "hotmail.it",
        "outlook.com", "live.com", "msn.com", "icloud.com", "me.com", "mac.com", "aol.com", "proton.me", "protonmail.com",
        "gmx.com", "gmx.de", "gmx.net", "web.de", "mail.com", "zoho.com", "yandex.com", "libero.it", "abv.bg", "mail.ru"
    };

    /// <summary>The domain of an email address, lowercased ("Ana@Oliva.AL" → "oliva.al"); null if there's none.</summary>
    public static string? Domain(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at < 1 || at == email!.Length - 1 ? null : email[(at + 1)..].Trim().ToLowerInvariant();
    }

    /// <summary>A company's own domain (not webmail), so two trials on it are probably one business.</summary>
    public static bool IsCompanyDomain(string? domain) => !string.IsNullOrEmpty(domain) && !FreeMail.Contains(domain);

    public static bool IsEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 256 && new EmailAddressAttribute().IsValid(email) && Domain(email)?.Contains('.') == true;

    /// <summary>The same rule as Identity's options in Program.cs: 8+ characters, a capital, a small letter, a digit.</summary>
    public static bool IsPassword(string? password) =>
        password is { Length: >= 8 and <= 128 } && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);

    /// <summary>Trimmed, inner spaces collapsed, control characters removed.</summary>
    public static string Clean(string? text) =>
        string.Join(' ', new string((text ?? "").Where(c => !char.IsControl(c)).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static bool IsName(string name) => name.Length is >= MinNameLength and <= MaxNameLength;

    /// <summary>A restaurant name that can make a menu link (has a letter or digit, within the length limits).</summary>
    public static bool IsRestaurant(string name) =>
        name.Length is >= MinRestaurantLength and <= MaxRestaurantLength && name.Any(char.IsLetterOrDigit);
}
