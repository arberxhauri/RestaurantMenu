using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Hubs;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>A device with the pairing code to show once (never stored in clear).</summary>
public sealed record PairingCode(StationDevice Device, string Code, DateTime ExpiresUtc);

/// <summary>
/// Station devices for one branch: set up, give a new pairing code, remove, pair a tablet, and
/// check a tablet's cookie. Callers check the person's permission first (ManageDevices);
/// every query here is also scoped to the branch, so an id from another branch finds nothing.
/// </summary>
public sealed class StationDeviceService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<StationDeviceService> _log;
    private readonly IHubContext<KitchenHub> _hub;
    private readonly StationConnections _connections;

    public StationDeviceService(ApplicationDbContext db, ILogger<StationDeviceService> log, IHubContext<KitchenHub> hub, StationConnections connections)
    {
        _db = db;
        _log = log;
        _hub = hub;
        _connections = connections;
    }

    public Task<List<StationDevice>> ListAsync(int branchId) =>
        _db.StationDevices.AsNoTracking().Where(d => d.BranchId == branchId)
            .OrderBy(d => d.Station).ThenBy(d => d.Name).ToListAsync();

    /// <returns>The new device with its code, or a message saying why not.</returns>
    public async Task<(PairingCode? Pairing, string? Problem)> CreateAsync(int branchId, string? name, ServiceStation station,
        string? userId, DateTime nowUtc)
    {
        var clean = StationDeviceRules.CleanName(name);
        if (clean == null) return (null, "Give the device a name, like \"Kitchen tablet\".");
        if (!StationDeviceRules.IsDeviceStation(station)) return (null, "Choose the kitchen or the bar.");
        if (await _db.StationDevices.CountAsync(d => d.BranchId == branchId) >= StationDeviceRules.MaxDevicesPerBranch)
            return (null, $"A branch can have {StationDeviceRules.MaxDevicesPerBranch} station devices. Remove one you no longer use first.");

        var device = new StationDevice
        {
            BranchId = branchId,
            Name = clean,
            Station = station,
            CreatedByUserId = userId,
            CreatedUtc = nowUtc
        };
        var code = OpenPairing(device, nowUtc);
        _db.StationDevices.Add(device);
        await _db.SaveChangesAsync();
        _log.LogInformation("Station device {DeviceId} ({Station}) set up for branch {BranchId}", device.Id, station, branchId);
        return (new PairingCode(device, code, device.PairingExpiresUtc!.Value), null);
    }

    /// <summary>A fresh code; the tablet signed in with the old pairing (if any) is signed out.</summary>
    public async Task<PairingCode?> NewCodeAsync(int branchId, int deviceId, DateTime nowUtc)
    {
        var device = await _db.StationDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.BranchId == branchId);
        if (device == null) return null;
        var code = OpenPairing(device, nowUtc);
        await _db.SaveChangesAsync();
        await DisconnectAsync(device);
        _log.LogInformation("Station device {DeviceId} given a new pairing code", deviceId);
        return new PairingCode(device, code, device.PairingExpiresUtc!.Value);
    }

    public async Task<StationDevice?> RemoveAsync(int branchId, int deviceId)
    {
        var device = await _db.StationDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.BranchId == branchId);
        if (device == null) return null;
        _db.StationDevices.Remove(device);
        await _db.SaveChangesAsync();
        await DisconnectAsync(device);
        _log.LogInformation("Station device {DeviceId} removed from branch {BranchId}", deviceId, branchId);
        return device;
    }

    /// <summary>Uses a pairing code: the device it belongs to, now signed in, or null for a wrong or old code.</summary>
    public async Task<StationDevice?> PairAsync(string? typedCode, DateTime nowUtc)
    {
        if (StationDeviceRules.NormalizeCode(typedCode) is not { } code) return null;
        var hash = StationDeviceRules.HashCode(code);
        var device = await _db.StationDevices.AsNoTracking().FirstOrDefaultAsync(d => d.PairingCodeHash == hash);
        if (device == null || !StationDeviceRules.CanPair(device.PairingCodeHash, device.PairingExpiresUtc, nowUtc)) return null;

        // One statement that only succeeds while the code is still open, so two tablets racing with
        // the same code can't both pair.
        var stamp = StationDeviceRules.NewStamp();
        var claimed = await _db.StationDevices
            .Where(d => d.Id == device.Id && d.PairingCodeHash == hash && d.PairingExpiresUtc > nowUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.PairingCodeHash, (string?)null)
                .SetProperty(d => d.PairingExpiresUtc, (DateTime?)null)
                .SetProperty(d => d.SessionStamp, stamp)
                .SetProperty(d => d.PairedUtc, nowUtc)
                .SetProperty(d => d.LastSeenUtc, nowUtc));
        if (claimed != 1) return null;

        _log.LogInformation("Station device {DeviceId} paired", device.Id);
        device.PairingCodeHash = null;
        device.PairingExpiresUtc = null;
        device.SessionStamp = stamp;
        device.PairedUtc = nowUtc;
        device.LastSeenUtc = nowUtc;
        return device;
    }

    /// <summary>Whether a device cookie is still good; notes when the tablet was last seen.</summary>
    public async Task<bool> ValidateAsync(StationDeviceIdentity identity, DateTime nowUtc)
    {
        var device = await _db.StationDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == identity.DeviceId && d.BranchId == identity.BranchId && d.Station == identity.Station);
        if (!StationDeviceRules.SessionValid(device, identity.Stamp)) return false;

        if (StationDeviceRules.ShouldRecordSeen(device!.LastSeenUtc, nowUtc))
        {
            await _db.StationDevices.Where(d => d.Id == device.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenUtc, nowUtc));
        }
        return true;
    }

    /// <summary>Cuts the device's open screens off: out of its station's group, and told it was signed out.</summary>
    private async Task DisconnectAsync(StationDevice device)
    {
        var group = KitchenHub.Group(device.BranchId, device.Station);
        foreach (var connectionId in _connections.Take(device.Id))
        {
            await _hub.Groups.RemoveFromGroupAsync(connectionId, group);
            await _hub.Clients.Client(connectionId).SendAsync(KitchenHub.SignedOut);
        }
    }

    private static string OpenPairing(StationDevice device, DateTime nowUtc)
    {
        var code = StationDeviceRules.NewCode();
        device.PairingCodeHash = StationDeviceRules.HashCode(code);
        device.PairingExpiresUtc = nowUtc + StationDeviceRules.PairingLifetime;
        device.SessionStamp = null;
        return code;
    }
}
