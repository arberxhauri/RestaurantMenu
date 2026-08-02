using Microsoft.AspNetCore.Mvc.Filters;

namespace RestaurantMenu.Filters;

/// <summary>
/// Sends "X-Robots-Tag: noindex, nofollow" for every response from the controller.
/// A header rather than a meta tag, because it also covers redirects, JSON endpoints
/// and file downloads — none of which can carry a &lt;meta&gt; tag.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class NoIndexAttribute : ActionFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        base.OnResultExecuting(context);
    }
}
