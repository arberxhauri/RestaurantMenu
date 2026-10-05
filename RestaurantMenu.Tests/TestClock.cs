namespace RestaurantMenu.Tests;

/// <summary>Builds UTC instants from wall-clock times in a branch's zone (Tirana by default).</summary>
internal static class TestClock
{
    public static readonly TimeZoneInfo Tirana = OpeningHours.Zone("Europe/Tirane");

    public static DateTime Utc(int y, int m, int d, int hour, int minute = 0, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, hour, minute, 0, DateTimeKind.Unspecified), zone ?? Tirana);

    public static BranchHours Hours(DayOfWeek day, int opens, int closes, int opensMin = 0, int closesMin = 0) =>
        new() { DayOfWeek = day, Opens = new TimeOnly(opens, opensMin), Closes = new TimeOnly(closes, closesMin) };
}
