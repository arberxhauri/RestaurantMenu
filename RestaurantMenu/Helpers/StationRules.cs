using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

public static class StationRules
{
    public static ServiceStation Effective(ServiceStation? item, ServiceStation category) => item ?? category;

    public static IReadOnlyList<ServiceStation> Split(IEnumerable<ServiceStation> assignments)
    {
        var combined = assignments.Aggregate(ServiceStation.None, (all, station) => all | station);
        return new[] { ServiceStation.Kitchen, ServiceStation.Bar }
            .Where(station => (combined & station) != 0).ToList();
    }

    /// <summary>The station a screen shows: "kitchen" (the default) or "bar"; null for anything else.</summary>
    public static ServiceStation? ParseStation(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "kitchen" => ServiceStation.Kitchen,
        "bar" => ServiceStation.Bar,
        _ => null
    };

    /// <summary>The column a ticket shows in on a station screen (the same words kitchen.js uses for orders).</summary>
    public static string ToScreen(TicketStatus status) => status switch
    {
        TicketStatus.New => "new",
        TicketStatus.Preparing => "preparing",
        TicketStatus.Cancelled => "cancelled",
        _ => "served" // ready or served: this station is done with it
    };

    /// <summary>What a screen's button means for its ticket; null for anything that isn't a column.</summary>
    public static TicketStatus? FromScreen(string? column) => column?.ToLowerInvariant() switch
    {
        "new" => TicketStatus.New,
        "preparing" => TicketStatus.Preparing,
        "served" => TicketStatus.Ready,
        "cancelled" => TicketStatus.Cancelled,
        _ => null
    };

    /// <summary>
    /// The order's status (what the guest's phone shows) from its tickets: preparing once any station has
    /// started, served when every station is done, cancelled only when every ticket is. Null without tickets
    /// (orders from before stations keep the status the kitchen gives them).
    /// </summary>
    public static OrderStatus? OrderStatusFor(IEnumerable<TicketStatus> tickets)
    {
        var all = tickets.ToList();
        if (all.Count == 0) return null;
        var active = all.Where(t => t != TicketStatus.Cancelled).ToList();
        if (active.Count == 0) return OrderStatus.Cancelled;
        if (active.All(t => t is TicketStatus.Ready or TicketStatus.Served)) return OrderStatus.Served;
        return active.Any(t => t != TicketStatus.New) ? OrderStatus.Preparing : OrderStatus.New;
    }

    public static bool AllReady(IEnumerable<TicketStatus> statuses)
    {
        var list = statuses.ToList();
        return list.Count > 0 && list.All(s => s is TicketStatus.Ready or TicketStatus.Served);
    }
}
