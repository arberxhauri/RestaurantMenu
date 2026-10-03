namespace RestaurantMenu.Models;

/// <summary>
/// One opening period of a branch on one weekday. A day can have several (lunch and
/// dinner); a day without any is closed. Closes &lt;= Opens means the period runs past
/// midnight into the next day, and Closes == Opens means open 24 hours. Times are wall
/// clock times in Branch.TimeZone.
/// </summary>
public class BranchHours
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly Opens { get; set; }
    public TimeOnly Closes { get; set; }
}
