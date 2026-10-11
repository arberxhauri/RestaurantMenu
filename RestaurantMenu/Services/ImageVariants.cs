using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace RestaurantMenu.Services;

/// <summary>
/// Smaller copies of uploaded photos (logos, banners, dishes), made on first request and kept on
/// the persistent disk next to the originals (images/_cache):
///   /images/banners/x.jpg?w=640  → WebP, 640 px wide (never wider than the original)
///   /images/banners/x.jpg?og=1   → JPEG, 1200×630, cropped to fill: link previews (og:image)
/// Uploaded files never change (a new upload gets a new name), so every answer is cached for a
/// year. The originals are still served as before. AVIF isn't offered: ImageSharp can't write it.
/// </summary>
public class ImageVariantsMiddleware
{
    public static readonly int[] Widths = { 160, 320, 480, 640, 960, 1280, 1600, 1920 };
    public const string Immutable = "public, max-age=31536000, immutable";

    private static readonly SemaphoreSlim Gate = new(2, 2);
    private readonly RequestDelegate _next;
    private readonly string _root;
    private readonly ILogger<ImageVariantsMiddleware> _logger;

    public ImageVariantsMiddleware(RequestDelegate next, ILogger<ImageVariantsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _root = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data", "images"));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? "";
        var hasWidth = int.TryParse(request.Query["w"], out var width);
        var og = request.Query["og"] == "1";
        if (!HttpMethods.IsGet(request.Method) || !path.StartsWith("/images/", StringComparison.Ordinal) || !hasWidth && !og
            || path.Contains("/_cache/", StringComparison.Ordinal))
        {
            await _next(context);
            return;
        }

        var source = Path.GetFullPath(Path.Combine(_root, Uri.UnescapeDataString(path["/images/".Length..])));
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (!source.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(source)
            || ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            // Unknown, outside the folder, or a GIF (animated): the normal static file answer.
            await _next(context);
            return;
        }

        if (!og)
        {
            // Snap to the nearest offered width so a crafted ?w= can't fill the disk.
            width = Widths.FirstOrDefault(w => w >= width, Widths[^1]);
        }
        var variant = og ? "og" : width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var relative = Path.GetRelativePath(_root, source);
        var target = Path.Combine(_root, "_cache", variant, relative + (og ? ".jpg" : ".webp"));

        if (!File.Exists(target))
        {
            await Gate.WaitAsync(context.RequestAborted);
            try
            {
                if (!File.Exists(target)) await MakeAsync(source, target, og ? null : width, context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Image variant failed for {Path}; serving the original", path);
                await _next(context);
                return;
            }
            finally
            {
                Gate.Release();
            }
        }

        context.Response.ContentType = og ? "image/jpeg" : "image/webp";
        context.Response.Headers.CacheControl = Immutable;
        context.Response.ContentLength = new FileInfo(target).Length;
        if (HttpMethods.IsGet(request.Method)) await context.Response.SendFileAsync(target, context.RequestAborted);
    }

    private static async Task MakeAsync(string source, string target, int? width, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var image = await Image.LoadAsync(source, ct);
        image.Mutate(x => x.AutoOrient());
        if (width is { } w)
        {
            if (image.Width > w) image.Mutate(x => x.Resize(w, 0));
        }
        else
        {
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1200, 630), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }));
        }
        image.Metadata.ExifProfile = null;
        var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        if (width != null) await image.SaveAsWebpAsync(temp, new WebpEncoder { Quality = 78 }, ct);
        else await image.SaveAsJpegAsync(temp, new JpegEncoder { Quality = 82 }, ct);
        File.Move(temp, target, overwrite: true);
    }
}
