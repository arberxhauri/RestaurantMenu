using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>A branch in the top bar's switcher.</summary>
public sealed record BranchChoice(int Id, string Name);

/// <summary>Everything the back office's frame needs: side panel, current page, branches.</summary>
public sealed record ShellModel(IReadOnlyList<NavGroup> Groups, string? ActiveKey,
    IReadOnlyList<BranchChoice> Branches, BranchChoice? Current);

/// <summary>
/// Builds the back office's side panel and branch switcher for the current request
/// (docs/PLAN-backoffice.md, phase 1). The branch is the page's own, else the one remembered in a
/// cookie, else the first; access is checked through <see cref="IBranchAccess"/> every time.
/// </summary>
public sealed class BackofficeShell
{
    public const string BranchCookie = "mqm_branch";
    private static readonly TimeSpan Remember = TimeSpan.FromDays(365);

    private readonly IBranchAccess _access;
    private readonly IHttpContextAccessor _http;

    public BackofficeShell(IBranchAccess access, IHttpContextAccessor http)
    {
        _access = access;
        _http = http;
    }

    public async Task<ShellModel> BuildAsync()
    {
        var http = _http.HttpContext ?? throw new InvalidOperationException("The back office frame needs a request.");
        var user = http.User;
        var isOwner = user.IsInRole("OWNER");
        var isAdmin = user.IsInRole("ADMIN");

        var branches = isOwner || user.IsInRole("STAFF")
            ? await _access.Branches(BranchPermission.View).AsNoTracking()
                .OrderBy(b => b.Name).Select(b => new BranchChoice(b.Id, b.Name)).ToListAsync()
            : new List<BranchChoice>();

        var route = http.Request.RouteValues;
        var controller = route["controller"]?.ToString();
        var action = route["action"]?.ToString();
        var fromRoute = BranchContextRules.BranchInRoute(controller, action,
            IntOrNull(route["id"]?.ToString()), IntOrNull(http.Request.Query["branchId"]));
        var remembered = IntOrNull(http.Request.Cookies[BranchCookie]);

        var currentId = BranchContextRules.Resolve(fromRoute, remembered, branches.Select(b => b.Id).ToList());
        var current = branches.FirstOrDefault(b => b.Id == currentId);
        if (currentId is { } id && id != remembered) RememberBranch(http, id);

        var role = currentId is { } b ? await _access.RoleAsync(b) : BranchRole.None;
        var modules = currentId is { } m ? await ModulesAsync(m) : new HashSet<BillingModule>();

        var groups = BackofficeNav.Build(new NavContext(currentId, role, isOwner, isAdmin, modules));
        return new ShellModel(groups, BackofficeNav.ActiveKey(controller, action), branches, current);
    }

    /// <summary>Keeps the chosen branch for the pages that don't name one (overview, billing…).</summary>
    public static void RememberBranch(HttpContext http, int branchId)
    {
        if (http.Response.HasStarted) return;
        http.Response.Cookies.Append(BranchCookie, branchId.ToString(System.Globalization.CultureInfo.InvariantCulture), new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = Remember,
            Path = "/"
        });
    }

    private async Task<HashSet<BillingModule>> ModulesAsync(int branchId)
    {
        var modules = new HashSet<BillingModule>();
        foreach (var module in Enum.GetValues<BillingModule>())
            if (await _access.HasModuleAsync(branchId, module)) modules.Add(module);
        return modules;
    }

    private static int? IntOrNull(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
}
