namespace RestaurantMenu.Tests;

/// <summary>
/// Kitchen and bar screens: each moves its own ticket of an order through the same three columns
/// (new, preparing, done), and the order's own status (what the guest's phone shows) follows its tickets.
/// </summary>
public class StationTicketRulesTests
{
    [Theory]
    [InlineData(TicketStatus.New, "new")]
    [InlineData(TicketStatus.Preparing, "preparing")]
    [InlineData(TicketStatus.Ready, "served")]
    [InlineData(TicketStatus.Served, "served")]
    [InlineData(TicketStatus.Cancelled, "cancelled")]
    public void A_ticket_shows_in_the_screens_column(TicketStatus status, string column) =>
        Assert.Equal(column, StationRules.ToScreen(status));

    [Theory]
    [InlineData("new", TicketStatus.New)]
    [InlineData("preparing", TicketStatus.Preparing)]
    [InlineData("served", TicketStatus.Ready)]
    [InlineData("cancelled", TicketStatus.Cancelled)]
    [InlineData("PREPARING", TicketStatus.Preparing)]
    public void A_button_on_the_screen_names_a_ticket_status(string column, TicketStatus status) =>
        Assert.Equal(status, StationRules.FromScreen(column));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ready")]
    [InlineData("2")]
    public void Anything_else_is_refused(string? column) => Assert.Null(StationRules.FromScreen(column));

    [Theory]
    [InlineData(null, ServiceStation.Kitchen)]
    [InlineData("", ServiceStation.Kitchen)]
    [InlineData("kitchen", ServiceStation.Kitchen)]
    [InlineData("bar", ServiceStation.Bar)]
    [InlineData("Bar", ServiceStation.Bar)]
    public void The_address_names_the_station(string? value, ServiceStation station) =>
        Assert.Equal(station, StationRules.ParseStation(value));

    [Theory]
    [InlineData("grill")]
    [InlineData("3")]
    [InlineData("kitchen,bar")]
    public void An_unknown_station_is_refused(string value) => Assert.Null(StationRules.ParseStation(value));

    [Fact]
    public void An_order_without_tickets_keeps_its_own_status() =>
        Assert.Null(StationRules.OrderStatusFor(Array.Empty<TicketStatus>()));

    [Theory]
    [InlineData(OrderStatus.New, TicketStatus.New, TicketStatus.New)]
    [InlineData(OrderStatus.Preparing, TicketStatus.Preparing, TicketStatus.New)]
    [InlineData(OrderStatus.Preparing, TicketStatus.Ready, TicketStatus.New)]
    [InlineData(OrderStatus.Served, TicketStatus.Ready, TicketStatus.Ready)]
    [InlineData(OrderStatus.Served, TicketStatus.Ready, TicketStatus.Served)]
    [InlineData(OrderStatus.Cancelled, TicketStatus.Cancelled, TicketStatus.Cancelled)]
    [InlineData(OrderStatus.Served, TicketStatus.Cancelled, TicketStatus.Ready)]
    [InlineData(OrderStatus.New, TicketStatus.Cancelled, TicketStatus.New)]
    public void The_order_follows_its_tickets(OrderStatus expected, TicketStatus kitchen, TicketStatus bar) =>
        Assert.Equal(expected, StationRules.OrderStatusFor(new[] { kitchen, bar }));

    [Fact]
    public void A_single_ticket_order_follows_that_ticket() =>
        Assert.Equal(OrderStatus.Served, StationRules.OrderStatusFor(new[] { TicketStatus.Ready }));
}
