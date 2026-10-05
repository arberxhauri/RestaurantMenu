using QRCoder;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// QR codes that open a branch's menu, optionally for one table (/menu/{slug}?t=14).
/// Pure .NET (no System.Drawing), so it runs in the Linux container.
/// </summary>
public class QrCodeService
{
    // Medium error correction: survives a scratch or a crease in a folded tent while
    // keeping the code coarse enough to scan from across a table.
    private const QRCodeGenerator.ECCLevel Ecc = QRCodeGenerator.ECCLevel.M;

    private readonly SeoService _seo;

    public QrCodeService(SeoService seo)
    {
        _seo = seo;
    }

    /// <summary>The link a code encodes. No language: guests choose theirs on the menu.</summary>
    /// <param name="code">The table's ordering code (Tables page), so the printed code can send orders.</param>
    public string MenuLink(Branch branch, int? table, string? code = null) => _seo.MenuUrl(branch.Slug, table: table, code: table == null ? null : code);

    /// <summary>
    /// SVG with a viewBox and no fixed size, so CSS sizes it. Includes the white quiet
    /// zone scanners need around the code.
    /// </summary>
    public string Svg(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, Ecc);
        return new SvgQRCode(data).GetGraphic(10, "#000000", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    /// <summary>PNG for designers and print shops; 20 px per module is about 700 px wide.</summary>
    public byte[] Png(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, Ecc);
        return new PngByteQRCode(data).GetGraphic(20);
    }
}
