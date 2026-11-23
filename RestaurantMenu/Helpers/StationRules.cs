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

    public static bool AllReady(IEnumerable<TicketStatus> statuses)
    {
        var list = statuses.ToList();
        return list.Count > 0 && list.All(s => s is TicketStatus.Ready or TicketStatus.Served);
    }
}
