using Microsoft.AspNetCore.Identity;

namespace RestaurantMenu.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; }
    public string NIPT { get; set; }
    public int NumberOfBranches { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
    public bool MustChangePassword { get; set; } = false;

    // Self-serve signup (SignupController). Accounts the admin creates keep the defaults and
    // count as approved; a self-serve account needs a confirmed email, and ApprovedUtc when
    // Signup__RequireApproval is on (or its trial was flagged for a look).
    public SignupSource SignupSource { get; set; } = SignupSource.Admin;
    /// <summary>ISO country code, e.g. AL. Billing becomes country-aware later.</summary>
    public string Country { get; set; } = "AL";
    /// <summary>The funnel's language for this person's emails and onboarding: en or sq.</summary>
    public string Language { get; set; } = "en";
    /// <summary>The Terms of use they accepted (TermsInfo.Version) and when.</summary>
    public string? TermsVersion { get; set; }
    public DateTime? TermsAcceptedUtc { get; set; }
    /// <summary>When the admin let a self-serve account in (or it was let in automatically). Null = waiting.</summary>
    public DateTime? ApprovedUtc { get; set; }
    /// <summary>Why a self-serve account waits for the admin, e.g. another trial on the same company domain.</summary>
    public string? ApprovalNote { get; set; }
    /// <summary>The restaurant name given at signup, offered when they create their first branch.</summary>
    public string? SignupRestaurantName { get; set; }
    /// <summary>Onboarding checklist: when they first opened a QR code to print, and when they hid the list.</summary>
    public DateTime? OnboardingQrUtc { get; set; }
    public DateTime? OnboardingDoneUtc { get; set; }

    /// <summary>Signed up on their own and hasn't confirmed their email yet.</summary>
    public bool AwaitingEmail => SignupSource == SignupSource.SelfServe && !EmailConfirmed;
    /// <summary>Signed up on their own, confirmed, and waiting for the admin.</summary>
    public bool AwaitingApproval => SignupSource == SignupSource.SelfServe && EmailConfirmed && ApprovedUtc == null;
        
    public ICollection<Branch> Branches { get; set; }
}

public enum SignupSource { Admin = 0, SelfServe = 1 }
