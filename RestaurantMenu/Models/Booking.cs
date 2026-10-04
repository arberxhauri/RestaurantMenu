namespace RestaurantMenu.Models;

/// <summary>
/// How a branch takes table bookings (Bookings → Settings). One row per branch, created
/// the first time a manager opens the settings. Slots follow the branch's opening hours.
/// </summary>
public class ReservationSettings
{
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public bool Enabled { get; set; }
    /// <summary>Minutes between bookable times: 15, 30 or 60.</summary>
    public int SlotMinutes { get; set; } = 30;
    /// <summary>Guests who may arrive in one slot (the kitchen's pace), across all bookings.</summary>
    public int CoversPerSlot { get; set; } = 20;
    /// <summary>Largest party bookable online; bigger groups are asked to call.</summary>
    public int MaxPartySize { get; set; } = 8;
    /// <summary>Minimum notice, in minutes.</summary>
    public int LeadMinutes { get; set; } = 120;
    public int MaxDaysAhead { get; set; } = 60;
    /// <summary>The last bookable time is this many minutes before closing.</summary>
    public int LastSeatingMinutes { get; set; } = 60;
    /// <summary>On: bookings are confirmed at once. Off: they wait for the restaurant to confirm.</summary>
    public bool AutoConfirm { get; set; } = true;
    /// <summary>Hours before the visit to send a reminder (0 = none).</summary>
    public int ReminderHours { get; set; } = 3;
    /// <summary>Digits added to local phone numbers ("069…" becomes +35569…).</summary>
    public string CountryCode { get; set; } = "355";
    /// <summary>Email the owner about every new online booking.</summary>
    public bool NotifyOwner { get; set; } = true;
    /// <summary>Days closed for online bookings (holidays, private events), "yyyy-MM-dd" comma separated.</summary>
    public string? ClosedDates { get; set; }
}

/// <summary>Persisted as int; never renumber.</summary>
public enum ReservationStatus
{
    /// <summary>Waiting for the restaurant to confirm (only when AutoConfirm is off).</summary>
    Pending = 0,
    Confirmed = 1,
    /// <summary>The guests arrived.</summary>
    Seated = 2,
    Cancelled = 3,
    NoShow = 4
}

public class Reservation
{
    public int Id { get; set; }
    /// <summary>The guest's handle in their confirmation link (view, add to calendar, cancel).</summary>
    public Guid PublicId { get; set; }
    /// <summary>Sent by the booking page with each attempt, so a retry never books twice.</summary>
    public Guid? ClientRequestId { get; set; }

    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = "";
    /// <summary>International format, e.g. +355691234567.</summary>
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public int Guests { get; set; }

    public DateTime StartsAtUtc { get; set; }
    /// <summary>The same moment as wall-clock time in the branch, for the day view and messages.</summary>
    public DateTime StartsAtLocal { get; set; }

    public ReservationStatus Status { get; set; }
    /// <summary>The guest's request ("a high chair, please").</summary>
    public string? Note { get; set; }
    /// <summary>Staff-only note.</summary>
    public string? StaffNote { get; set; }
    public string Language { get; set; } = "en";
    /// <summary>"online" from the booking page, "staff" when added in the back office.</summary>
    public string Source { get; set; } = "online";
    /// <summary>Who cancelled: "guest" or "restaurant".</summary>
    public string? CancelledBy { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ReminderSentUtc { get; set; }
}
