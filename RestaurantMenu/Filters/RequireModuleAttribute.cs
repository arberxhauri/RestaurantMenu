using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Filters;

/// <summary>A module's state on the page being shown, for the layout's "not in your plan" card.</summary>
public record ModuleGate(BillingModule Module, bool On, bool Paused, bool AccountReadOnly)
{
    public const string Key = "ModuleGate";

    public string Title => Paused ? "This branch is paused"
        : AccountReadOnly ? "Your account is read-only"
        : $"{EntitlementRules.Name(Module)} isn't in your plan";

    public string Text => Paused
        ? "Your plan includes fewer branches than you have, so this one is paused. You can look around; nothing here can be changed. Its menu stays online."
        : AccountReadOnly
            ? "You can look at everything here, but changes are paused until the plan is renewed."
            : Module switch
            {
                BillingModule.Ordering => "Guests can still make a list and show it to their server. Add table ordering to your plan to take orders from the table and use the kitchen display. Your tables and past orders are kept.",
                BillingModule.Bookings => "The booking page tells guests to call you instead. Add bookings to your plan to take bookings online again. Bookings already made are below, and you can still update them.",
                BillingModule.Website => "Your website and its address now open your menu. Add the website to your plan to publish it again. Everything you wrote is kept.",
                BillingModule.OwnDomain => "Your own domain now opens your menu on our address. Add an own domain to your plan to use it again.",
                BillingModule.Management => "Stock, recipes, shifts and sales can be looked at but not changed. Add management to your plan to use them again. Nothing is deleted.",
                _ => "Add it to your plan to use it again."
            };
}

/// <summary>
/// On a back-office controller whose route has the branch as {id} (branch/{id}/tables,
/// kitchen/{id}, ...): pages always open, with a <see cref="ModuleGate"/> for the layout when the
/// branch's plan doesn't include the module; changes are refused with the reason, unless the
/// action is marked <see cref="AllowWhenModuleOffAttribute"/> (finishing what already exists).
/// Who may act on the branch at all is still IBranchAccess's decision, in the action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireModuleAttribute : Attribute, IAsyncActionFilter
{
    public BillingModule Module { get; }

    public RequireModuleAttribute(BillingModule module) => Module = module;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!int.TryParse(context.RouteData.Values["id"]?.ToString(), out var branchId))
        {
            await next();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var e = await services.GetRequiredService<IEntitlementService>().ForBranchAsync(branchId);
        if (e == null || e.Has(Module))
        {
            await next();
            return;
        }

        var gate = new ModuleGate(Module, false, e.Paused, !e.Account.CanWrite);
        if (context.Controller is Controller controller && !controller.ViewData.ContainsKey(ModuleGate.Key))
        {
            controller.ViewData[ModuleGate.Key] = gate;
        }

        var exempt = context.ActionDescriptor.EndpointMetadata.OfType<AllowWhenModuleOffAttribute>().Any();
        if (!AccountGate.IsWrite(context.HttpContext.Request) || exempt)
        {
            await next();
            return;
        }

        // A read-only account or paused branch: AccountGateFilter's message says why.
        var message = gate.AccountReadOnly || gate.Paused
            ? AccountGate.Reason(e)
            : $"{EntitlementRules.Name(Module)} isn't in your plan, so this can't be changed. The owner can add it on the Billing page.";
        var request = context.HttpContext.Request;
        if (AccountGate.WantsJson(request))
        {
            context.Result = new ObjectResult(new { ok = false, message }) { StatusCode = StatusCodes.Status409Conflict };
            return;
        }
        var tempData = services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context.HttpContext);
        tempData["Error"] = message;
        var referer = request.Headers.Referer.ToString();
        context.Result = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == request.Host.Host
            ? new RedirectResult(uri.PathAndQuery)
            : new RedirectToActionResult("Index", "Dashboard", null);
    }
}

/// <summary>This change is allowed while the module is off: it finishes or removes what already exists.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowWhenModuleOffAttribute : Attribute { }
