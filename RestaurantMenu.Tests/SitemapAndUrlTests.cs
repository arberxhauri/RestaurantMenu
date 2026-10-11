using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>Sitemap XML against the official schemas, URL hygiene redirects and HEAD handling.</summary>
public class SitemapAndUrlTests
{
    private static SitemapService.Entry Entry(string url, string[] langs, string[]? images = null) =>
        new(url, langs.Select(l => new SeoAlternate(l, l == "en" ? "https://myquickmenu.com/menu/oliva" : $"https://myquickmenu.com/menu/oliva?lang={l}")).ToList(),
            "https://myquickmenu.com/menu/oliva", new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc), images ?? Array.Empty<string>());

    private static XmlSchemaSet Schemas([CallerFilePath] string here = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(here)!, "Schemas");
        var set = new XmlSchemaSet { XmlResolver = null };
        foreach (var f in new[] { "sitemap.xsd", "siteindex.xsd", "sitemap-image.xsd", "xhtml-link.xsd" })
            set.Add(null, XmlReader.Create(Path.Combine(dir, f), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }));
        set.Compile();
        return set;
    }

    private static void Validate(XDocument doc)
    {
        var problems = new List<string>();
        doc.Validate(Schemas(), (_, e) => problems.Add(e.Message));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Url_sets_with_hreflang_and_images_are_valid_sitemaps()
    {
        var doc = SitemapService.UrlSet(new[]
        {
            Entry("https://myquickmenu.com/menu/oliva", new[] { "en", "sq" }, new[] { "https://myquickmenu.com/images/banners/a.jpg" }),
            Entry("https://myquickmenu.com/menu/oliva?lang=sq", new[] { "en", "sq" })
        }, "weekly");
        Validate(doc);
        var xml = doc.ToString();
        Assert.Contains("<lastmod>2026-10-11T10:00:00Z</lastmod>", xml);
        Assert.Contains("hreflang=\"x-default\"", xml);
        Assert.Contains("<image:loc>https://myquickmenu.com/images/banners/a.jpg</image:loc>", xml);
    }

    [Fact]
    public void Single_language_pages_have_no_hreflang()
    {
        var doc = SitemapService.UrlSet(new[] { Entry("https://myquickmenu.com/menu/oliva", new[] { "en" }) }, "weekly");
        Validate(doc);
        Assert.DoesNotContain("hreflang", doc.ToString());
    }

    [Fact]
    public void Every_translation_names_all_the_others_and_itself()
    {
        var entries = new[] { "en", "sq", "it" }.Select(l => Entry(l == "en" ? "https://myquickmenu.com/menu/oliva" : $"https://myquickmenu.com/menu/oliva?lang={l}", new[] { "en", "sq", "it" })).ToList();
        var doc = SitemapService.UrlSet(entries, "weekly");
        var ns = SitemapService.Ns;
        foreach (var url in doc.Root!.Elements(ns + "url"))
        {
            var loc = url.Element(ns + "loc")!.Value;
            var hrefs = url.Elements(SitemapService.Xhtml + "link").Select(l => l.Attribute("href")!.Value).ToList();
            Assert.Contains(loc, hrefs);
            Assert.Equal(4, hrefs.Count);   // en, sq, it and x-default
        }
    }

    private static async Task<HttpContext> Run(Func<RequestDelegate, object> middleware, string method, string path, string query = "", string host = "myquickmenu.com")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Host = new HostString(host);
        context.Response.Body = new MemoryStream();
        var reached = false;
        var m = middleware(ctx => { reached = true; ctx.Response.StatusCode = 200; return ctx.Response.WriteAsync("body"); });
        await (Task)m.GetType().GetMethod("InvokeAsync")!.Invoke(m, new object[] { context })!;
        context.Items["reached"] = reached;
        return context;
    }

    private static IConfiguration Config(bool redirectRender = false) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Seo:BaseUrl"] = "https://myquickmenu.com",
        ["Seo:RedirectRenderHost"] = redirectRender ? "true" : "false"
    }).Build();

    [Theory]
    [InlineData("/pricing/", "", "/pricing")]
    [InlineData("/Menu/Oliva-Kitchen", "?t=4", "/menu/oliva-kitchen?t=4")]
    [InlineData("/FAQ/", "?lang=sq", "/faq?lang=sq")]
    public async Task One_spelling_per_page(string path, string query, string location)
    {
        var ctx = await Run(next => new UrlHygieneMiddleware(next, Config()), "GET", path, query);
        Assert.Equal(301, ctx.Response.StatusCode);
        Assert.Equal(location, ctx.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("GET", "/images/products/Photo_Of_Pizza.JPG")]   // uploaded file names keep their case
    [InlineData("GET", "/mqm-Key-123456.txt")]                    // the IndexNow key file
    [InlineData("GET", "/")]
    [InlineData("POST", "/Account/Login/")]                       // only GET/HEAD are redirected
    public async Task Files_and_posts_are_left_alone(string method, string path)
    {
        var ctx = await Run(next => new UrlHygieneMiddleware(next, Config()), method, path);
        Assert.True((bool)ctx.Items["reached"]!);
    }

    [Fact]
    public async Task The_render_host_redirects_only_when_switched_on()
    {
        var off = await Run(next => new UrlHygieneMiddleware(next, Config()), "GET", "/menu/oliva", host: "restaurantmenu-7fzm.onrender.com");
        Assert.True((bool)off.Items["reached"]!);
        var on = await Run(next => new UrlHygieneMiddleware(next, Config(redirectRender: true)), "GET", "/menu/oliva", host: "restaurantmenu-7fzm.onrender.com");
        Assert.Equal("https://myquickmenu.com/menu/oliva", on.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Head_is_answered_like_get_without_a_body()
    {
        var ctx = await Run(next => new HeadAsGetMiddleware(next), "HEAD", "/llms.txt");
        Assert.True((bool)ctx.Items["reached"]!);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal(0, ((MemoryStream)ctx.Response.Body).Length);
        Assert.Equal("HEAD", ctx.Request.Method);
    }

    [Theory]
    [InlineData(false, false, "index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1")]
    [InlineData(true, false, "noindex, follow")]
    [InlineData(true, true, "noindex, nofollow")]
    public void Robots_meta(bool noIndex, bool noFollow, string expected) =>
        Assert.Equal(expected, new SeoMetadata { NoIndex = noIndex, NoFollow = noFollow }.RobotsContent);
}
