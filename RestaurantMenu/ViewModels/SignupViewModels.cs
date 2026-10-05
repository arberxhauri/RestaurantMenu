using RestaurantMenu.Helpers;

namespace RestaurantMenu.ViewModels;

/// <summary>/pricing: presets, the build-your-plan form and its quote.</summary>
public record PricingPage(
    SignupText.Words W,
    string Lang,
    PlanSelection Selection,
    Quote Quote,
    IReadOnlyList<(string Key, PlanSelection Selection, Quote Quote)> Presets,
    IReadOnlyDictionary<(Models.BillingModule, Models.BillingInterval), int> Prices,
    string Currency,
    int TrialDays);

/// <summary>What the signup form posts. Plan fields (m, b, i) come along from /pricing.</summary>
public class SignupForm
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Restaurant { get; set; }
    public string? Country { get; set; }
    public bool Terms { get; set; }
    /// <summary>Honeypot: hidden from people, filled by bots.</summary>
    public string? Website { get; set; }
    /// <summary>When the form was shown (signed), to turn away instant bot posts.</summary>
    public string? T { get; set; }
    public string? Lang { get; set; }
    public List<string>? M { get; set; }
    public string? B { get; set; }
    public string? I { get; set; }
}

/// <summary>/signup: the form, the plan it carries, and any problems.</summary>
public record SignupPage(
    SignupText.Words W,
    string Lang,
    SignupForm Form,
    PlanSelection Plan,
    Quote Quote,
    int TrialDays,
    string FormTime,
    string MenuBase,
    (string Slug, bool Taken)? Link,
    IReadOnlyDictionary<string, string> Errors,
    string? Problem,
    bool Busy);

/// <summary>The pages after the form: check your email, confirm, waiting for approval, link problems.</summary>
public record SignupStatusPage(SignupText.Words W, string Lang, string Kind, string? Email = null, string? UserId = null, string? Token = null, bool Sent = false, bool Failed = false, bool Busy = false);
