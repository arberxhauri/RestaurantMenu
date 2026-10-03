using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RestaurantMenu.Filters;

/// <summary>
/// Refuses a request body over <paramref name="bytes"/> with 413 before model binding
/// reads it. [RequestSizeLimit] alone makes the read throw, which ends up in the error
/// log for every oversized post; this answers quietly and sets the same hard limit for
/// bodies without a Content-Length.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class MaxBodySizeAttribute : Attribute, IResourceFilter
{
    private readonly long _bytes;
    public MaxBodySizeAttribute(long bytes) => _bytes = bytes;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var http = context.HttpContext;
        if (http.Request.ContentLength > _bytes)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
            return;
        }
        var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = _bytes;
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
