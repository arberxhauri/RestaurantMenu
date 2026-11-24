using System.Collections.Concurrent;

namespace RestaurantMenu.Services;

/// <summary>
/// The live kitchen-hub connections of each station device, so removing a device (or giving it a new
/// code) cuts its open screens off at once instead of when their socket next reconnects. In memory:
/// the app runs as one instance, and a restart drops every connection anyway.
/// </summary>
public sealed class StationConnections
{
    private readonly ConcurrentDictionary<string, int> _deviceByConnection = new();

    public void Track(int deviceId, string connectionId) => _deviceByConnection[connectionId] = deviceId;

    public void Forget(string connectionId) => _deviceByConnection.TryRemove(connectionId, out _);

    /// <summary>The device's connections, which stop being tracked.</summary>
    public IReadOnlyList<string> Take(int deviceId)
    {
        var connections = _deviceByConnection.Where(p => p.Value == deviceId).Select(p => p.Key).ToList();
        foreach (var id in connections) _deviceByConnection.TryRemove(id, out _);
        return connections;
    }
}
