using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantMenu.Models;

namespace RestaurantMenu.Filters;

/// <summary>
/// A signed-in user flagged MustChangePassword can only reach the change-password page
/// (and sign out) until they pick a new password. Public pages are unaffected.
/// </summary>
public sealed class RequirePasswordChangeFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> OpenControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account", "Home", "Menu", "Seo"
    };

    private readonly UserManager<ApplicationUser> _userManager;

    public RequirePasswordChangeFilter(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        if (context.HttpContext.User.Identity?.IsAuthenticated == true && !OpenControllers.Contains(controller))
        {
            var user = await _userManager.GetUserAsync(context.HttpContext.User);
            if (user?.MustChangePassword == true)
            {
                context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
                return;
            }
        }

        await next();
    }
}
