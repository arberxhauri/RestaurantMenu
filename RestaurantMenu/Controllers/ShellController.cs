using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>The top bar's branch switcher: remembers the branch and stays in the same section.</summary>
[Authorize(Roles = "OWNER,STAFF")]
[Route("switch-branch")]
public class ShellController : Controller
{
    private readonly IBranchAccess _access;

    public ShellController(IBranchAccess access) => _access = access;

    /// <param name="to">The side-panel page to open for the new branch (BackofficeNav keys only).</param>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Switch(int id, string? to)
    {
        var role = await _access.RoleAsync(id);
        if (role == BranchRole.None) return NotFound();

        BackofficeShell.RememberBranch(HttpContext, id);

        // The same page if the person may open it on that branch, else their role's own screen.
        var everything = Enum.GetValues<BillingModule>().ToHashSet();
        var page = BackofficeNav.Build(new NavContext(id, role, User.IsInRole("OWNER"), User.IsInRole("ADMIN"), everything))
            .SelectMany(g => g.Items)
            .FirstOrDefault(i => string.Equals(i.Key, to, StringComparison.Ordinal));
        return LocalRedirect(page?.Href ?? BranchPermissions.Home(role, id));
    }
}
