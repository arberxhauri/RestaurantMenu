namespace RestaurantMenu.Models;

/// <summary>
/// A shared kitchen or bar tablet that signs in to one station screen without anyone's password
/// (docs/PLAN-backoffice.md §5, phase 8). A manager creates it and gets a one-time pairing code;
/// the tablet enters the code at /station and keeps a device cookie. Only hashes are stored.
/// Removing the device, or giving it a new code, signs the tablet out on its next request.
/// </summary>
public class StationDevice
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Kitchen or Bar: the one screen this device opens.</summary>
    public ServiceStation Station { get; set; } = ServiceStation.Kitchen;

    /// <summary>SHA-256 (hex) of the pairing code while one is open; null once used.</summary>
    public string? PairingCodeHash { get; set; }
    public DateTime? PairingExpiresUtc { get; set; }

    /// <summary>Random value carried in the device cookie; null until paired. A new one signs older cookies out.</summary>
    public string? SessionStamp { get; set; }
    public DateTime? PairedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public enum StationDeviceState
{
    /// <summary>Signed in on a tablet.</summary>
    Paired,
    /// <summary>A pairing code is open.</summary>
    Waiting,
    /// <summary>The code ran out before the tablet used it; give it a new one.</summary>
    CodeExpired
}
