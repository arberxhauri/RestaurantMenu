namespace RestaurantMenu.Tests;

/// <summary>The live floor plan: what colour each table shows, and which layouts can be saved.</summary>
public class TableRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 20, 0, 0);

    private static Reservation Booking(int minutesFromNow, ReservationStatus status = ReservationStatus.Confirmed) =>
        new() { StartsAtLocal = Now.AddMinutes(minutesFromNow), Status = status };

    [Fact]
    public void A_table_without_orders_or_bookings_is_free() =>
        Assert.Equal(LiveTableStatus.Free, TableStatusRules.Resolve(null, Array.Empty<Reservation>(), Now));

    [Fact]
    public void An_open_order_makes_the_table_occupied() =>
        Assert.Equal(LiveTableStatus.Occupied, TableStatusRules.Resolve(new Order(), Array.Empty<Reservation>(), Now));

    [Fact]
    public void A_requested_bill_wins_over_occupied() =>
        Assert.Equal(LiveTableStatus.BillRequested,
            TableStatusRules.Resolve(new Order { BillRequestedUtc = Now }, new[] { Booking(30) }, Now));

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(120)]
    public void A_booking_from_half_an_hour_ago_to_two_hours_ahead_reserves_the_table(int minutes) =>
        Assert.Equal(LiveTableStatus.Reserved, TableStatusRules.Resolve(null, new[] { Booking(minutes) }, Now));

    [Theory]
    [InlineData(-31)]
    [InlineData(121)]
    public void Bookings_outside_that_window_leave_the_table_free(int minutes) =>
        Assert.Equal(LiveTableStatus.Free, TableStatusRules.Resolve(null, new[] { Booking(minutes) }, Now));

    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Seated)]
    public void Only_pending_or_confirmed_bookings_reserve_a_table(ReservationStatus status) =>
        Assert.Equal(LiveTableStatus.Free, TableStatusRules.Resolve(null, new[] { Booking(15, status) }, Now));

    [Theory]
    [InlineData(TableShape.Round, 4, 10, 20, 0)]
    [InlineData(TableShape.Rectangle, 1, 0, 0, 90)]
    [InlineData(TableShape.Square, 50, 100, 100, 270)]
    public void Sensible_layouts_can_be_saved(TableShape shape, int seats, double x, double y, int rotation) =>
        Assert.True(TableLayoutRules.IsValid(shape, seats, (decimal)x, (decimal)y, rotation));

    [Theory]
    [InlineData(0, 0, 10, 10, 0)]     // no seats
    [InlineData(0, 51, 10, 10, 0)]    // too many seats
    [InlineData(0, 4, -1, 10, 0)]     // off the left
    [InlineData(0, 4, 10, 101, 0)]    // off the bottom
    [InlineData(0, 4, 10, 10, 45)]    // only quarter turns
    [InlineData(99, 4, 10, 10, 0)]    // unknown shape
    public void Impossible_layouts_are_refused(int shape, int seats, double x, double y, int rotation) =>
        Assert.False(TableLayoutRules.IsValid((TableShape)shape, seats, (decimal)x, (decimal)y, rotation));
}
