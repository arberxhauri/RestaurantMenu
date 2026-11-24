using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>A signed-in station device, as its cookie says (checked against the database on every request).</summary>
public sealed record StationDeviceIdentity(int DeviceId, int BranchId, ServiceStation Station, string Stamp);

/// <summary>
/// The station device sign-in: its own cookie scheme, separate from people's. Only the station
/// screens and the kitchen hub accept it (<see cref="Policy"/>); every other page uses the default
/// scheme and never sees a device. A device has no user id, so it can never pass as a person.
/// </summary>
public static class StationAuth
{
    public const string Scheme = "Station";
    /// <summary>People (owner or staff) or a station device: the station screens and the kitchen hub.</summary>
    public const string Policy = "StaffOrStation";
    public const string Role = "STATION";
    public const string CookieName = ".mqm.station";

    private const string DeviceClaim = "station:device";
    private const string BranchClaim = "station:branch";
    private const string StationClaim = "station:station";
    private const string StampClaim = "station:stamp";
    /// <summary>Antiforgery's per-user id; deliberately not the Identity user-id claim.</summary>
    private const string SubjectClaim = "sub";

    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(90);

    public static ClaimsPrincipal Principal(StationDevice device)
    {
        var claims = new[]
        {
            new Claim(DeviceClaim, device.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(BranchClaim, device.BranchId.ToString(CultureInfo.InvariantCulture)),
            new Claim(StationClaim, ((int)device.Station).ToString(CultureInfo.InvariantCulture)),
            new Claim(StampClaim, device.SessionStamp ?? ""),
            new Claim(SubjectClaim, $"station:{device.Id}"),
            new Claim(ClaimTypes.Name, device.Name),
            new Claim(ClaimTypes.Role, Role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>The device in this principal, only from an identity this scheme signed in.</summary>
    public static StationDeviceIdentity? Read(ClaimsPrincipal? principal)
    {
        var identity = principal?.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == Scheme);
        if (identity == null) return null;
        return Int(identity, DeviceClaim) is { } device && Int(identity, BranchClaim) is { } branch
               && Int(identity, StationClaim) is { } station && identity.FindFirst(StampClaim)?.Value is { Length: > 0 } stamp
            ? new StationDeviceIdentity(device, branch, (ServiceStation)station, stamp)
            : null;
    }

    private static int? Int(ClaimsIdentity identity, string type) =>
        int.TryParse(identity.FindFirst(type)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>The device cookie and the "people or devices" policy.</summary>
    public static void AddStationDevices(this IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = CookieLifetime;
            options.SlidingExpiration = true;
            // People's sign-in page answers challenges on shared pages; the device page is /station.
            options.Events.OnRedirectToLogin = _ => Task.CompletedTask;
            options.Events.OnRedirectToAccessDenied = _ => Task.CompletedTask;
            options.Events.OnValidatePrincipal = ValidateAsync;
        });

        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme, IdentityConstants.ApplicationScheme)
            .RequireRole("OWNER", "STAFF", Role)));
    }

    /// <summary>Signs the tablet out once its device is removed, re-paired, or its branch deleted.</summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var device = Read(context.Principal);
        var devices = context.HttpContext.RequestServices.GetRequiredService<StationDeviceService>();
        if (device != null && await devices.ValidateAsync(device, DateTime.UtcNow)) return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(Scheme);
    }
}
