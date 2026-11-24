using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Hubs;

/// <summary>
/// Live updates for kitchen displays. A screen joins its branch's group once access is
/// checked (same rule as the page: Editor and up); the server then pushes "order" (a new
/// or changed order) and "paused". Screens only listen; changes go through KitchenController.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
public class KitchenHub : Hub
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public KitchenHub(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public static string Group(int branchId) => $"kitchen-{branchId}";

    /// <summary>One group per station screen; the kitchen keeps the original name.</summary>
    public static string Group(int branchId, ServiceStation station) =>
        station == ServiceStation.Bar ? $"bar-{branchId}" : Group(branchId);

    /// <summary>
    /// Joins a station's updates ("kitchen" or "bar"). Returns false (and joins nothing) when this
    /// person may not see the branch's orders or the station is unknown.
    /// </summary>
    public async Task<bool> Join(int branchId, string? station)
    {
        var userId = Context.User == null ? null : _users.GetUserId(Context.User);
        if (userId == null || StationRules.ParseStation(station) is not { } s) return false;
        var role = await BranchAccess.RoleOfAsync(_db, userId, branchId);
        if (!BranchAccess.Allows(role, BranchPermission.Kitchen)) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(branchId, s));
        return true;
    }
}
