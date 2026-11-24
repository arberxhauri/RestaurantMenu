using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

public enum LiveTableStatus { Free, Reserved, Occupied, BillRequested }

public static class TableStatusRules
{
    public static LiveTableStatus Resolve(Order? openOrder, IEnumerable<Reservation> reservations, DateTime localNow)
    {
        if (openOrder?.BillRequestedUtc != null) return LiveTableStatus.BillRequested;
        if (openOrder != null) return LiveTableStatus.Occupied;

        var soon = reservations.Any(r =>
            r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed &&
            r.StartsAtLocal >= localNow.AddMinutes(-30) &&
            r.StartsAtLocal <= localNow.AddHours(2));
        return soon ? LiveTableStatus.Reserved : LiveTableStatus.Free;
    }

    public static string Label(LiveTableStatus status) => status switch
    {
        LiveTableStatus.BillRequested => "Bill requested",
        LiveTableStatus.Occupied => "Occupied",
        LiveTableStatus.Reserved => "Reserved soon",
        _ => "Free"
    };
}

/// <summary>What the floor-plan editor may store for a table (positions are percentages of the area).</summary>
public static class TableLayoutRules
{
    public const int MaxSeats = 50;

    public static bool IsValid(TableShape shape, int seats, decimal x, decimal y, int rotation) =>
        Enum.IsDefined(shape)
        && seats is >= 1 and <= MaxSeats
        && x is >= 0 and <= 100
        && y is >= 0 and <= 100
        && rotation is 0 or 90 or 180 or 270;
}
