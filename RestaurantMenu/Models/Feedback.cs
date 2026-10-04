namespace RestaurantMenu.Models;

/// <summary>Persisted as int; never renumber.</summary>
public enum FeedbackStatus
{
    New = 0,
    Read = 1,
    /// <summary>Dealt with (e.g. the owner called the guest back).</summary>
    Resolved = 2
}

/// <summary>
/// A guest's rating of their visit, from the menu footer. Anonymous unless the guest leaves a
/// way to reach them (only asked after a low rating). Visible to managers and the owner.
/// </summary>
public class Feedback
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>1 to 5 stars.</summary>
    public int Rating { get; set; }
    public string? Comment { get; set; }
    /// <summary>Optional phone or email the guest gave so the restaurant can get back to them.</summary>
    public string? Contact { get; set; }
    /// <summary>The table, when the menu was opened from a table's QR code.</summary>
    public int? TableNumber { get; set; }
    public string Language { get; set; } = "en";
    public FeedbackStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
}
