using System.Security.Cryptography;
using System.Text;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Helpers;

/// <summary>The rules for station devices: pairing codes, what a device may open, and when it counts as connected.</summary>
public static class StationDeviceRules
{
    public const int CodeLength = 8;
    public const int MaxNameLength = 80;
    public const int MaxDevicesPerBranch = 20;

    /// <summary>No I, L, O, 0 or 1: nothing that reads two ways on a tablet keyboard.</summary>
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SeenGranularity = TimeSpan.FromMinutes(5);
    private const int StampBytes = 32;

    public static string NewCode() => RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);

    public static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(StampBytes));

    /// <summary>A code as typed ("abcd-efgh", with spaces): upper case without separators; null if it can't be a code.</summary>
    public static string? NormalizeCode(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var code = new string(typed.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return code.Length == CodeLength && code.All(c => CodeAlphabet.Contains(c)) ? code : null;
    }

    public static string FormatCode(string code) => $"{code[..(CodeLength / 2)]}-{code[(CodeLength / 2)..]}";

    public static string HashCode(string normalizedCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode)));

    public static bool CanPair(string? codeHash, DateTime? expiresUtc, DateTime nowUtc) =>
        codeHash != null && expiresUtc is { } expires && expires > nowUtc;

    public static bool IsDeviceStation(ServiceStation station) => station is ServiceStation.Kitchen or ServiceStation.Bar;

    /// <summary>The role a device acts with: its own station's, nothing else.</summary>
    public static BranchRole RoleFor(ServiceStation station) => station switch
    {
        ServiceStation.Kitchen => BranchRole.Kitchen,
        ServiceStation.Bar => BranchRole.Bar,
        _ => BranchRole.None
    };

    public static string ScreenPath(int branchId, ServiceStation station) =>
        BranchPermissions.Home(RoleFor(station), branchId);

    /// <summary>Last seen is a hint for the device list, not a log: written at most every few minutes.</summary>
    public static bool ShouldRecordSeen(DateTime? lastSeenUtc, DateTime nowUtc) =>
        lastSeenUtc is not { } seen || nowUtc - seen >= SeenGranularity;

    public static string? CleanName(string? typed)
    {
        var name = typed?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }

    public static StationDeviceState State(StationDevice device, DateTime nowUtc) =>
        device.SessionStamp != null ? StationDeviceState.Paired
        : CanPair(device.PairingCodeHash, device.PairingExpiresUtc, nowUtc) ? StationDeviceState.Waiting
        : StationDeviceState.CodeExpired;

    /// <summary>Whether a device cookie with this stamp still belongs to the device.</summary>
    public static bool SessionValid(StationDevice? device, string? stamp) =>
        device?.SessionStamp is { } current && stamp != null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(stamp));
}
