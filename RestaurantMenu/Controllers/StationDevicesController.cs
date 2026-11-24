using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Configuration → Station devices (/branch/{id}/devices): managers set up the shared kitchen and
/// bar tablets, give one a new pairing code (signing the old pairing out), or remove it. A new
/// code is shown once, on the response to the change, and never stored in clear.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[RequireModule(BillingModule.Ordering)]
[Route("branch/{id:int}/devices")]
public class StationDevicesController : Controller
{
    private readonly IBranchAccess _access;
    private readonly StationDeviceService _devices;
    private readonly QrCodeService _qr;
    private readonly UserManager<ApplicationUser> _users;

    public StationDevicesController(IBranchAccess access, StationDeviceService devices, QrCodeService qr, UserManager<ApplicationUser> users)
    {
        _access = access;
        _devices = devices;
        _qr = qr;
        _users = users;
    }

    private Task<Branch?> BranchAsync(int id) =>
        _access.Branches(BranchPermission.ManageDevices).AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await BranchAsync(id);
        return branch == null ? NotFound() : await PageAsync(branch, null);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int id, string? name, ServiceStation station)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var (pairing, problem) = await _devices.CreateAsync(id, name, station, _users.GetUserId(User), DateTime.UtcNow);
        if (problem != null)
        {
            TempData["Error"] = problem;
            return RedirectToAction(nameof(Index), new { id });
        }
        return await PageAsync(branch, pairing);
    }

    [HttpPost("{deviceId:int}/code")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewCode(int id, int deviceId)
    {
        var branch = await BranchAsync(id);
        if (branch == null) return NotFound();
        var pairing = await _devices.NewCodeAsync(id, deviceId, DateTime.UtcNow);
        return pairing == null ? NotFound() : await PageAsync(branch, pairing);
    }

    [HttpPost("{deviceId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id, int deviceId)
    {
        if (await BranchAsync(id) == null) return NotFound();
        var removed = await _devices.RemoveAsync(id, deviceId);
        if (removed == null) return NotFound();
        TempData["Success"] = $"{removed.Name} was removed. That screen is signed out the next time it refreshes.";
        return RedirectToAction(nameof(Index), new { id });
    }

    private async Task<IActionResult> PageAsync(Branch branch, PairingCode? pairing)
    {
        Response.Headers.CacheControl = "no-store"; // a pairing code is on the page
        var link = pairing == null ? null
            : Url.Action("Index", "Station", new { code = StationDeviceRules.FormatCode(pairing.Code) }, Request.Scheme);
        var page = new StationDevicesPage(branch, await _devices.ListAsync(branch.Id), pairing, link,
            link == null ? null : _qr.Svg(link), OpeningHours.Zone(branch.TimeZone), DateTime.UtcNow);
        return View("Index", page);
    }
}

public sealed record StationDevicesPage(Branch Branch, IReadOnlyList<StationDevice> Devices, PairingCode? Pairing,
    string? PairLink, string? PairQrSvg, TimeZoneInfo Zone, DateTime NowUtc);
