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
