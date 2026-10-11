using RestaurantMenu.Services;

namespace RestaurantMenu.Helpers;

/// <summary>
/// URLs of the resized copies of uploaded photos (ImageVariantsMiddleware) for img src/srcset and
/// og:image. Anything that isn't an upload (wwwroot images, external URLs) is left as it is.
/// </summary>
public static class ImageUrls
{
    /// <summary>An uploaded photo that can be resized (GIFs may be animated; they're served as they are).</summary>
    public static bool IsUpload(string? url) =>
        url != null && url.StartsWith("/images/", StringComparison.Ordinal) && !url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>Spaces and commas would break a srcset list; old uploads kept the guest's file name.</summary>
    private static string Safe(string url) => url.Replace(" ", "%20").Replace(",", "%2C");

    /// <summary>The photo at (about) this width, as WebP.</summary>
    public static string Sized(string url, int width) => IsUpload(url) ? $"{Safe(url)}?w={width}" : url;

    /// <summary>"…?w=320 320w, …?w=640 640w" for these widths, or null for a photo that isn't an upload.</summary>
    public static string? Srcset(string? url, params int[] widths) =>
        IsUpload(url) ? string.Join(", ", (widths.Length == 0 ? ImageVariantsMiddleware.Widths : widths).Select(w => $"{Safe(url!)}?w={w} {w}w")) : null;

    /// <summary>The 1200×630 crop for link previews, with its size; the original for anything else.</summary>
    public static (string Url, int? Width, int? Height) Share(string url) =>
        IsUpload(url) ? ($"{Safe(url)}?og=1", 1200, 630) : (url, null, null);
}
