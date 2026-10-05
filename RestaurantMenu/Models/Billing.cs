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

/// <summary>
/// A price for one module, valid from a date. Never edited: a new price is a new row, so the
/// history stays (Admin → Plans &amp; prices). <see cref="Withdrawn"/>: from this date the module
/// has no price ("price on request").
/// </summary>
public class PriceBook
{
    public int Id { get; set; }
    public BillingModule Module { get; set; }
    public BillingInterval Interval { get; set; }
    public string Currency { get; set; } = "EUR";
    public int UnitAmountCents { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public bool Withdrawn { get; set; }
    /// <summary>The admin who set it; null for the first-start seed.</summary>
    public string? ChangedById { get; set; }
    public string? PaddlePriceId { get; set; }
}

/// <summary>
/// The platform's plan settings, one row (Id 1), edited in Admin → Plans &amp; prices. On the very
/// first start it is created from configuration (Billing__…, Signup__…); after that the database
/// is the only source, so changes need no deploy.
/// </summary>
public class PlanSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    /// <summary>ISO code of the price book in use, e.g. EUR.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Length of a free trial, from when the account opens.</summary>
    public int TrialDays { get; set; } = 14;
    /// <summary>Staff accounts included per branch on plans that aren't legacy (one can override it per owner).</summary>
    public int SeatsPerBranch { get; set; } = 5;
    /// <summary>Days a past-due account keeps working before it becomes read-only.</summary>
    public int GraceDays { get; set; } = 14;
    /// <summary>/pricing and /signup are open (they also need working email).</summary>
    public bool SignupEnabled { get; set; }
    /// <summary>New self-serve accounts wait for the admin after confirming their email.</summary>
    public bool SignupRequireApproval { get; set; } = true;

    // Invoices (phase 3). The seller is the platform's operator; these go on every invoice.
    public string? OperatorName { get; set; }
    public string? OperatorNipt { get; set; }
    public string? OperatorAddress { get; set; }
    public string? OperatorEmail { get; set; }
    /// <summary>Where bank transfers go.</summary>
    public string? OperatorIban { get; set; }
    public string? OperatorBank { get; set; }
    public string? OperatorSwift { get; set; }
    /// <summary>VAT added on invoices, in percent (0 while the operator isn't VAT-registered).</summary>
    public decimal VatPercent { get; set; }
    /// <summary>Days to pay a first invoice.</summary>
    public int InvoiceDueDays { get; set; } = 14;
    /// <summary>A renewal invoice is issued this many days before the paid period ends (and is due on that day).</summary>
    public int RenewalLeadDays { get; set; } = 7;

    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedById { get; set; }
    public uint Version { get; set; }

    /// <summary>Bank transfer can be offered: the seller's name and IBAN are filled in.</summary>
    public bool CanInvoice => !string.IsNullOrWhiteSpace(OperatorName) && !string.IsNullOrWhiteSpace(OperatorIban);
}

public enum InvoiceStatus { Open = 1, Paid = 2, Void = 3 }

/// <summary>
/// An invoice for one billing period. Issued by the operator (bank transfer) or mirrored from a
/// card provider later. Everything printed on it is copied in when it's issued (seller, buyer,
/// prices), so later changes never alter an invoice already sent.
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    /// <summary>MQM-2026-0042: sequential per year, never reused. Also the bank transfer reference.</summary>
    public string Number { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Sequence { get; set; }

    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }
    public int SubscriptionId { get; set; }

    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public BillingInterval Interval { get; set; }
    public int BranchQuantity { get; set; }
    /// <summary>first (after a trial or a lapse) or renewal.</summary>
    public string Kind { get; set; } = "first";

    public string Currency { get; set; } = "EUR";
    public int SubtotalCents { get; set; }
    public decimal VatPercent { get; set; }
    public int VatCents { get; set; }
    public int TotalCents { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;
    public DateTime IssuedUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public DateTime? PaidUtc { get; set; }
    public string? PaidNote { get; set; }
    public DateTime? VoidedUtc { get; set; }
    public BillingProvider Provider { get; set; } = BillingProvider.BankTransfer;
    public string? ProviderRef { get; set; }
    /// <summary>The tax authority's code once the invoice is fiscalized (filled by hand for now).</summary>
    public string? FiscalCode { get; set; }
    /// <summary>The invoice's language (the owner's: en or sq).</summary>
    public string Language { get; set; } = "en";

    public string SellerName { get; set; } = string.Empty;
    public string? SellerNipt { get; set; }
    public string? SellerAddress { get; set; }
    public string? SellerIban { get; set; }
    public string? SellerBank { get; set; }
    public string? SellerSwift { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string? BuyerNipt { get; set; }
    public string? BuyerAddress { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}

public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public BillingModule Module { get; set; }
    public int Quantity { get; set; }
    public int UnitCents { get; set; }
    public int TotalCents { get; set; }
}

/// <summary>The last invoice number used in a year (one row per year, bumped atomically).</summary>
public class InvoiceSequence
{
    public int Year { get; set; }
    public int Last { get; set; }
}

public enum BillingEventType { PaymentSucceeded = 1, PaymentFailed = 2, SubscriptionCanceled = 3, SubscriptionUpdated = 4 }

/// <summary>
/// The billing inbox: every message about money (an admin marking a bank transfer paid now, a
/// card provider's webhook later) is stored here first, then applied exactly once by
/// BillingWorker. Unique on (Provider, EventId), so a repeated message is a no-op.
/// </summary>
public class BillingEvent
{
    public int Id { get; set; }
    public BillingProvider Provider { get; set; }
    public string EventId { get; set; } = string.Empty;
    public BillingEventType Type { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime ReceivedUtc { get; set; }
    public DateTime? ProcessedUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Billing emails already sent: unique on (owner, kind, period), so a restart never sends one twice.</summary>
public class BillingEmailLog
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public DateTime SentUtc { get; set; }
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

    // A change the owner asked for that starts with the next paid period (bank transfer):
    // modules as "Ordering,Bookings", the branch count, monthly or yearly. Null: no change.
    public string? NextModules { get; set; }
    public int? NextBranchQuantity { get; set; }
    public BillingInterval? NextInterval { get; set; }
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
