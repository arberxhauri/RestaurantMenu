using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// /station: a shared kitchen or bar tablet enters the pairing code a manager made on Station
/// devices and is signed in to that one station screen (its own cookie, StationAuth). The code
/// only ever signs in on a POST, so a link can't sign a browser in by itself.
/// </summary>
[NoIndex]
[Route("station")]
public class StationController : Controller
{
    private readonly StationDeviceService _devices;
    private readonly ILogger<StationController> _log;

    public StationController(StationDeviceService devices, ILogger<StationController> log)
    {
        _devices = devices;
        _log = log;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? code)
    {
        Response.Headers.CacheControl = "no-store";
        return View(new StationPairPage(code, null, await CurrentDeviceAsync()));
    }

    [HttpPost("")]
    [EnableRateLimiting("station-pair")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pair(string? code)
    {
        if (StationDeviceRules.NormalizeCode(code) == null)
        {
            return View("Index", new StationPairPage(code, "Enter the 8-character code shown on Station devices, like ABCD-EFGH.", null));
        }

        var device = await _devices.PairAsync(code, DateTime.UtcNow);
        if (device == null)
        {
            _log.LogInformation("Station pairing refused: unknown, used or expired code");
            return View("Index", new StationPairPage(code,
                "That code doesn't work. It may have been used already or be more than 30 minutes old: ask a manager for a new one.", null));
        }

        await HttpContext.SignInAsync(StationAuth.Scheme, StationAuth.Principal(device),
            new AuthenticationProperties { IsPersistent = true });
        return LocalRedirect(StationDeviceRules.ScreenPath(device.BranchId, device.Station));
    }

    [HttpPost("signout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutDevice()
    {
        await HttpContext.SignOutAsync(StationAuth.Scheme);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>The device this browser is signed in as (checked against the database), if any.</summary>
    private async Task<StationScreen?> CurrentDeviceAsync()
    {
        var result = await HttpContext.AuthenticateAsync(StationAuth.Scheme);
        return StationAuth.Read(result.Principal) is { } device
            ? new StationScreen(result.Principal!.Identity?.Name ?? "This screen", StationDeviceRules.ScreenPath(device.BranchId, device.Station))
            : null;
    }
}

public sealed record StationScreen(string Name, string Path);

public sealed record StationPairPage(string? Code, string? Error, StationScreen? Current);
