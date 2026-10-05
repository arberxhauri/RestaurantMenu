namespace RestaurantMenu.Models;

// Plans and subscriptions (docs/PLAN-signup-payments.md). One subscription per owner; it is the
// single source of what the owner's account may do, read through IEntitlementService only.
// Money is integer cents. Phase 1: no payments yet, the admin sets everything by hand.

/// <summary>What a restaurant can switch on. Menu is the base and always on.</summary>
public enum BillingModule
{
    /// <summary>Guest menu, QR codes, allergens, schedules, specials, branding, offline, analytics, feedback, translate-assist.</summary>
    Menu = 0,
    /// <summary>Table ordering and the kitchen display.</summary>
    Ordering = 1,
    /// <summary>The booking page and the bookings back office.</summary>
    Bookings = 2,
    /// <summary>/site/{slug} and the free subdomain.</summary>
    Website = 3,
    /// <summary>Stock, recipes, shifts, sales and the /manage overview.</summary>
    Management = 4,
    /// <summary>The restaurant's own domains on its website (needs Website). Quantity = domains.</summary>
    OwnDomain = 5,
    /// <summary>Booking texts through the platform's SMS gateway (needs Bookings).</summary>
    Sms = 6
}

public enum BillingInterval { Month = 0, Year = 1 }

/// <summary>
/// The stored state. The state that applies right now also depends on the clock (a trial that
/// has ended, a period that ran out): EntitlementRules.EffectiveStatus works that out.
/// </summary>
public enum SubscriptionStatus { Trialing = 0, Active = 1, PastDue = 2, ReadOnly = 3, Cancelled = 4 }

public enum BillingProvider { None = 0, BankTransfer = 1, Paddle = 2 }

/// <summary>A price for one module, valid from a date. Never edited: a new price is a new row.</summary>
public class PriceBook
{
    public int Id { get; set; }
    public BillingModule Module { get; set; }
    public BillingInterval Interval { get; set; }
    public string Currency { get; set; } = "EUR";
    public int UnitAmountCents { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public string? PaddlePriceId { get; set; }
}

public class Subscription
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    public SubscriptionStatus Status { get; set; }
    public BillingInterval Interval { get; set; }
    public string Currency { get; set; } = "EUR";
    /// <summary>Branches the account may have live; multiplies every per-branch module.</summary>
    public int BranchQuantity { get; set; } = 1;
    /// <summary>Staff per branch; null = the default (Billing__SeatsPerBranch). Legacy: no limit.</summary>
    public int? SeatsPerBranch { get; set; }

    public DateTime? TrialEndsUtc { get; set; }
    public DateTime? CurrentPeriodStartUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }
    public DateTime? GraceEndsUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }

    public BillingProvider Provider { get; set; }
    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }

    /// <summary>
    /// Customers from before plans existed: every module, their old branch allowance, no charge,
    /// until the owner of the platform moves them (with notice).
    /// </summary>
    public bool IsLegacy { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Postgres xmin: two admins (or later a webhook) saving at once can't overwrite each other.</summary>
    public uint Version { get; set; }

    public ICollection<SubscriptionItem> Items { get; set; } = new List<SubscriptionItem>();
}

/// <summary>A module switched on, priced when it was added (0 for legacy and trials).</summary>
public class SubscriptionItem
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
    public BillingModule Module { get; set; }
    /// <summary>Branches for per-branch modules; domains for OwnDomain.</summary>
    public int Quantity { get; set; } = 1;
    public int UnitAmountCents { get; set; }
}

/// <summary>What goes on invoices. Filled in on the billing page (phase 3).</summary>
public class BillingProfile
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }
    public string? LegalName { get; set; }
    public string? Nipt { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "AL";
    public string? BillingEmail { get; set; }
    public bool VatRegistered { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Who changed a subscription, and from what to what (JSON snapshots).</summary>
public class SubscriptionAudit
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    /// <summary>The admin or owner who made the change; null for the system (startup, worker).</summary>
    public string? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromJson { get; set; }
    public string? ToJson { get; set; }
    public DateTime AtUtc { get; set; }
}
