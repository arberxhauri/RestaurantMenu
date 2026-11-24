using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Tests;

public class StationRulesTests
{
    [Fact]
    public void Item_override_wins_and_null_inherits_category()
    {
        Assert.Equal(ServiceStation.Bar, StationRules.Effective(ServiceStation.Bar, ServiceStation.Kitchen));
        Assert.Equal(ServiceStation.Kitchen, StationRules.Effective(null, ServiceStation.Kitchen));
    }

    [Fact]
    public void A_mixed_order_gets_one_ticket_for_each_station()
    {
        var result = StationRules.Split(new[] { ServiceStation.Kitchen, ServiceStation.Bar, ServiceStation.Kitchen | ServiceStation.Bar });
        Assert.Equal(new[] { ServiceStation.Kitchen, ServiceStation.Bar }, result);
    }

    [Fact]
    public void No_station_means_no_station_ticket() => Assert.Empty(StationRules.Split(new[] { ServiceStation.None }));

    [Fact]
    public void An_order_is_ready_only_when_every_station_is_ready_or_served()
    {
        Assert.True(StationRules.AllReady(new[] { TicketStatus.Ready, TicketStatus.Served }));
        Assert.False(StationRules.AllReady(new[] { TicketStatus.Ready, TicketStatus.Preparing }));
        Assert.False(StationRules.AllReady(Array.Empty<TicketStatus>()));
    }
}

public class StationPermissionTests
{
    [Theory]
    [InlineData(ServiceStation.Kitchen, RestaurantMenu.Services.BranchPermission.Kitchen)]
    [InlineData(ServiceStation.Bar, RestaurantMenu.Services.BranchPermission.Bar)]
    public void Each_screen_needs_its_own_stations_permission(ServiceStation station, RestaurantMenu.Services.BranchPermission permission) =>
        Assert.Equal(permission, StationRules.Permission(station));
}

/// <summary>Kitchen and bar move single dishes; the station's ticket follows its dishes.</summary>
public class ItemTicketRulesTests
{
    private static TicketStatus? For(params TicketStatus[] items) => StationRules.TicketStatusFor(items);

    [Fact]
    public void No_items_means_no_ticket_status() => Assert.Null(For());

    [Fact]
    public void All_new_is_new() => Assert.Equal(TicketStatus.New, For(TicketStatus.New, TicketStatus.New));

    [Fact]
    public void One_dish_started_starts_the_ticket() =>
        Assert.Equal(TicketStatus.Preparing, For(TicketStatus.New, TicketStatus.Preparing));

    [Fact]
    public void One_dish_ready_and_one_waiting_is_still_preparing() =>
        Assert.Equal(TicketStatus.Preparing, For(TicketStatus.Ready, TicketStatus.New));

    [Fact]
    public void Every_dish_ready_makes_the_ticket_ready() =>
        Assert.Equal(TicketStatus.Ready, For(TicketStatus.Ready, TicketStatus.Ready));

    [Fact]
    public void Ready_and_served_dishes_are_ready() =>
        Assert.Equal(TicketStatus.Ready, For(TicketStatus.Ready, TicketStatus.Served));

    [Fact]
    public void Every_dish_served_is_served() =>
        Assert.Equal(TicketStatus.Served, For(TicketStatus.Served, TicketStatus.Served));

    [Fact]
    public void Cancelled_dishes_are_left_out() =>
        Assert.Equal(TicketStatus.Ready, For(TicketStatus.Cancelled, TicketStatus.Ready));

    [Fact]
    public void Only_cancelled_dishes_is_cancelled() =>
        Assert.Equal(TicketStatus.Cancelled, For(TicketStatus.Cancelled, TicketStatus.Cancelled));

    [Theory]
    [InlineData(TicketStatus.New, new[] { TicketStatus.New })]
    [InlineData(TicketStatus.Preparing, new[] { TicketStatus.Preparing })]
    [InlineData(TicketStatus.Ready, new[] { TicketStatus.Ready, TicketStatus.Served })]
    [InlineData(TicketStatus.Cancelled, new[] { TicketStatus.Cancelled })]
    public void A_screen_column_holds_these_states(TicketStatus column, TicketStatus[] states) =>
        Assert.Equal(states, StationRules.StatesIn(column));
}

public class ItemIdListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_list_means_the_whole_card(string? value) => Assert.Empty(StationRules.ParseItemIds(value)!);

    [Fact]
    public void A_list_is_read_without_repeats() =>
        Assert.Equal(new[] { 12, 13 }, StationRules.ParseItemIds(" 12, 13,12 "));

    [Theory]
    [InlineData("12,x")]
    [InlineData("-4")]
    [InlineData("0")]
    [InlineData("1.5")]
    public void Anything_but_ids_is_refused(string value) => Assert.Null(StationRules.ParseItemIds(value));

    [Fact]
    public void A_huge_list_is_refused() =>
        Assert.Null(StationRules.ParseItemIds(string.Join(",", Enumerable.Range(1, StationRules.MaxItemsPerMove + 1))));
}
