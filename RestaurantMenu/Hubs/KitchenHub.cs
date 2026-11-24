using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Hubs;

/// <summary>
/// Live updates for kitchen displays. A screen joins its station's group once access is
/// checked (the same rule as the page: BranchPermission.Kitchen or .Bar, for a person or a
/// paired station device); the server then pushes "order" (a new or changed order) and
/// "paused". Screens only listen; changes go through KitchenController.
/// </summary>
[Authorize(Policy = StationAuth.Policy)]
public class KitchenHub : Hub
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly StationDeviceService _devices;
    private readonly StationConnections _connections;

    public KitchenHub(ApplicationDbContext db, UserManager<ApplicationUser> users, StationDeviceService devices, StationConnections connections)
    {
        _db = db;
        _users = users;
        _devices = devices;
        _connections = connections;
    }

    /// <summary>Tells a removed or re-coded device's open screen it was signed out (it also leaves its group).</summary>
    public const string SignedOut = "signedout";

    public static string Group(int branchId) => $"kitchen-{branchId}";

    /// <summary>One group per station screen; the kitchen keeps the original name.</summary>
    public static string Group(int branchId, ServiceStation station) =>
        station == ServiceStation.Bar ? $"bar-{branchId}" : Group(branchId);

    /// <summary>
    /// Joins a station's updates ("kitchen" or "bar"). Returns false (and joins nothing) when this
    /// person or device may not see that station's orders, or the station is unknown.
    /// </summary>
    public async Task<bool> Join(int branchId, string? station)
    {
        if (StationRules.ParseStation(station) is not { } s) return false;
        var userId = Context.User == null ? null : _users.GetUserId(Context.User);
        var device = userId == null ? StationAuth.Read(Context.User) : null;
        if (userId == null && device == null) return false;
        // The socket may have outlived the device: check its row again, not just the cookie from connect.
        if (device != null && !await _devices.ValidateAsync(device, DateTime.UtcNow)) return false;
        var role = await BranchAccess.RoleOfAsync(_db, userId, device, branchId);
        if (!BranchPermissions.Allows(role, StationRules.Permission(s))) return false;
        if (device != null) _connections.Track(device.DeviceId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(branchId, s));
        return true;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Forget(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
