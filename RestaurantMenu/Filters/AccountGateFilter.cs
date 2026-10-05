using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Filters;

/// <summary>
/// Read-only accounts and paused branches in the back office. For a change (any request but
/// GET/HEAD) by a signed-in owner or staff member it works out, once, which of their branches
/// can't be changed right now, for IBranchAccess to leave out. If none of their branches can
/// be changed, the request stops here with the reason. If an action then refuses because of
/// it (IBranchAccess says not found), the reason is shown instead: a flash message and back to
/// the page, or a 409 with { message } for script requests. Looking at pages always works;
/// public pages, sign-in and the admin are untouched.
/// </summary>
public sealed class AccountGateFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> OpenControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account", "Admin", "Home", "Menu", "Seo", "Book", "Site", "Signup",
        // Paying is how a read-only account gets fixed.
        "Billing"
    };

    private readonly IEntitlementService _entitlements;
    private readonly IBranchAccess _access;
    private readonly ITempDataDictionaryFactory _tempData;

    public AccountGateFilter(IEntitlementService entitlements, IBranchAccess access, ITempDataDictionaryFactory tempData)
    {
        _entitlements = entitlements;
        _access = access;
        _tempData = tempData;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        if (http.User.Identity?.IsAuthenticated != true || !AccountGate.IsWrite(http.Request)
            || OpenControllers.Contains(controller) || http.User.IsInRole("ADMIN"))
        {
            await next();
            return;
        }

        // Every live branch this person works on, by owner.
        var branches = await _access.Branches(BranchPermission.View).AsNoTracking()
            .Select(b => new { b.Id, b.UserId }).ToListAsync();
        var readOnly = new HashSet<int>();
        string? reason = null;
        foreach (var owner in branches.GroupBy(b => b.UserId))
        {
            var account = await _entitlements.ForOwnerAsync(owner.Key);
            if (!account.CanWrite)
            {
                readOnly.UnionWith(owner.Select(b => b.Id));
                reason ??= AccountGate.ReadOnlyRefusal;
                continue;
            }
            var paused = await _entitlements.PausedBranchesAsync(owner.Key);
            readOnly.UnionWith(paused);
            if (paused.Count > 0 && reason == null)
            {
                var e = await _entitlements.ForBranchAsync(paused.First());
                if (e != null) reason = AccountGate.Reason(e);
            }
        }
        http.Items[AccountGate.ReadOnlyBranches] = readOnly;

        // Nothing they work on can be changed: stop here. An owner whose own account is
        // read-only also can't add or restore branches (those name no branch yet).
        var nothingWritable = branches.Count > 0 && branches.All(b => readOnly.Contains(b.Id));
        var addsBranch = controller.Equals("Branch", StringComparison.OrdinalIgnoreCase)
                         && context.ActionDescriptor.RouteValues["action"] is "Create" or "Restore";
        if (!nothingWritable && addsBranch && http.User.IsInRole("OWNER") && UserId(context) is { } ownerId)
        {
            var own = await _entitlements.ForOwnerAsync(ownerId);
            if (!own.CanWrite)
            {
                nothingWritable = true;
                reason = AccountGate.ReadOnlyRefusal;
            }
        }
        if (nothingWritable)
        {
            context.Result = Refuse(context, reason ?? "This can't be changed right now.");
            return;
        }

        // An action refused because of the plan: say why instead of "not found".
        var executed = await next();
        var message = http.Items[AccountGate.Blocked] as string;
        if (message == null && readOnly.Count > 0 && executed.Result is NotFoundResult or NotFoundObjectResult)
        {
            message = reason;
        }
        if (message != null)
        {
            executed.Result = Refuse(context, message);
        }
    }

    private static string? UserId(ActionExecutingContext context) =>
        context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>The refusal: JSON for scripts, otherwise a flash message and back where they came from.</summary>
    private IActionResult Refuse(FilterContext context, string message)
    {
        var request = context.HttpContext.Request;
        if (AccountGate.WantsJson(request))
        {
            return new ObjectResult(new { ok = false, message }) { StatusCode = StatusCodes.Status409Conflict };
        }

        var tempData = _tempData.GetTempData(context.HttpContext);
        tempData["Error"] = message;
        var referer = request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == request.Host.Host)
        {
            return new RedirectResult(uri.PathAndQuery);
        }
        return new RedirectToActionResult("Index", "Dashboard", null);
    }
}
