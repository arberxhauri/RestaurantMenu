using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Serves robots.txt and sitemap.xml. Both are generated rather than static files so
/// the sitemap tracks branches as they are created and the absolute URLs always match
/// whatever host the site is actually served from.
/// </summary>
public class SeoController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly SeoService _seo;

    /// <summary>Areas that sit behind auth and must never be crawled.</summary>
    private static readonly string[] DisallowedPaths =
    {
        "/account/",
        "/admin/",
        "/branch/",
        "/category/",
        "/dashboard/",
        "/product/",
        "/home/error"
    };

    public SeoController(ApplicationDbContext context, SeoService seo)
    {
        _context = context;
        _seo = seo;
    }

    [Route("robots.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots()
    {
        var builder = new StringBuilder();
        builder.AppendLine("User-agent: *");

        foreach (var path in DisallowedPaths)
        {
            builder.AppendLine($"Disallow: {path}");
        }

        builder.AppendLine("Allow: /");
        builder.AppendLine();
        builder.AppendLine($"Sitemap: {_seo.Url("/sitemap.xml")}");

        return Content(builder.ToString(), "text/plain", Encoding.UTF8);
    }

    [Route("sitemap.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap()
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";

        var root = new XElement(ns + "urlset",
            new XAttribute(XNamespace.Xmlns + "xhtml", xhtml));

        root.Add(Url(ns, _seo.Url("/"), "weekly", "1.0"));
        root.Add(Url(ns, _seo.Url("/home/privacy"), "yearly", "0.3"));

        var branches = await _context.Branches
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.Name)
            .ToListAsync();

        foreach (var branch in branches)
        {
            var languages = (branch.SupportedLanguages ?? SeoService.DefaultLanguage)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToList();

            if (languages.Count == 0)
            {
                languages.Add(SeoService.DefaultLanguage);
            }

            // One <url> per language, and each entry cross-links every translation so
            // Google treats them as one page in several languages rather than duplicates.
            foreach (var language in languages)
            {
                var entry = Url(ns, _seo.MenuUrl(branch.Name, language), "weekly", "0.8");

                if (languages.Count > 1)
                {
                    foreach (var alternate in languages)
                    {
                        entry.Add(new XElement(xhtml + "link",
                            new XAttribute("rel", "alternate"),
                            new XAttribute("hreflang", alternate),
                            new XAttribute("href", _seo.MenuUrl(branch.Name, alternate))));
                    }
                }

                root.Add(entry);
            }
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return Content(document.Declaration + document.ToString(), "application/xml", Encoding.UTF8);
    }

    private static XElement Url(XNamespace ns, string location, string changeFrequency, string priority) =>
        new(ns + "url",
            new XElement(ns + "loc", location),
            new XElement(ns + "changefreq", changeFrequency),
            new XElement(ns + "priority", priority));
}
