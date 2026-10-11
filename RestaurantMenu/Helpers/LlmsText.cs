using System.Text;
using RestaurantMenu.Helpers.Landing;
using RestaurantMenu.Services;

namespace RestaurantMenu.Helpers;

/// <summary>
/// /llms.txt (the llmstxt.org format: an H1, a one-paragraph summary in a blockquote, then
/// sections of links with one-line descriptions) and /llms-full.txt (the full text of every
/// content page in English and Albanian), for AI assistants that read a site's own summary.
/// Built from the same content, prices and brand facts as the pages, so they never disagree.
/// </summary>
public static class LlmsText
{
    public record Input(
        string BaseUrl,
        BrandOptions Brand,
        IReadOnlyDictionary<string, string> TokensEn,
        IReadOnlyDictionary<string, string> TokensSq,
        int Restaurants);

    public static string Summary(Input i)
    {
        var sb = new StringBuilder();
        var en = LandingContent.All("en");
        var sq = LandingContent.All("sq");
        string Url(string path, string lang = "en") => i.BaseUrl + LandingContent.Localize(path, lang);
        string F(string text) => LandingContent.Fill(text, i.TokensEn);

        sb.Append("# My Quick Menu\n\n");
        sb.Append("> ").Append(i.TokensEn["definition"]).Append("\n\n");
        sb.Append($"My Quick Menu is made by {i.Brand.ParentName} ({i.Brand.ParentUrl}) in {i.Brand.Locality}, Albania. ");
        sb.Append($"Contact: {i.Brand.Email}");
        if (!string.IsNullOrWhiteSpace(i.Brand.Phone)) sb.Append($", {i.Brand.Phone}");
        sb.Append(". The site and guest pages are in English and Albanian; guest menus can be in English, Albanian, Italian, German, French, Spanish and Turkish.\n\n");

        sb.Append("## Product pages\n\n");
        foreach (var key in new[] { "digital-menu", "restaurant-website", "table-bookings", "restaurant-management" })
            sb.Append($"- [{en[key].H1}]({Url(en[key].Path)}): {F(en[key].MetaDescription)}\n");

        sb.Append("\n## Pricing\n\n");
        var t = i.TokensEn;
        sb.Append($"- [Pricing]({Url("/pricing")}): Prices per branch per month, without VAT. Digital menu {t["menu"]}; ");
        sb.Append($"Menu, website and bookings {t["guests"]}; Everything {t["everything"]}. ");
        sb.Append($"Add-on modules: website {t["website"]}, online bookings {t["bookings"]}, table ordering with kitchen display {t["ordering"]}, ");
        sb.Append($"management (stock, shifts, sales) {t["management"]}, own domain {t["domain"]} each. {t["trial"]}-day free trial, no card needed.\n");

        sb.Append("\n## FAQ\n\n");
        sb.Append($"- [Frequently asked questions]({Url("/faq")}): {F(en["faq"].MetaDescription)}\n");

        sb.Append("\n## About and contact\n\n");
        sb.Append($"- [About My Quick Menu]({Url("/about")}): {F(en["about"].MetaDescription)}\n");
        sb.Append($"- [Contact]({Url("/contact")}): {F(en["contact"].MetaDescription)}\n");

        sb.Append("\n## Restaurant directory\n\n");
        sb.Append($"- [Restaurants with online menus]({Url("/restaurants")}): {i.Restaurants} restaurants, cafés and bars using My Quick Menu, by city, with links to each menu (current prices, photos, allergens), website and booking page.\n");

        sb.Append("\n## Shqip (Albanian)\n\n");
        foreach (var key in new[] { "digital-menu", "restaurant-website", "table-bookings", "restaurant-management", "faq", "about", "contact" })
            sb.Append($"- [{sq[key].H1}]({Url(sq[key].Path, "sq")}): {LandingContent.Fill(sq[key].MetaDescription, i.TokensSq)}\n");
        sb.Append($"- [Çmimet]({Url("/pricing", "sq")}): çmimet e planeve dhe moduleve, për degë në muaj.\n");

        sb.Append("\n## Optional\n\n");
        sb.Append($"- [Full text of these pages]({i.BaseUrl}/llms-full.txt): every product page, the FAQ, about and contact, in English and Albanian.\n");
        sb.Append($"- [Sitemap]({i.BaseUrl}/sitemap.xml): every page, including each restaurant's menu in each of its languages.\n");
        return sb.ToString();
    }

    public static string Full(Input i)
    {
        var sb = new StringBuilder();
        sb.Append("# My Quick Menu: full text\n\n");
        sb.Append("> ").Append(i.TokensEn["definition"]).Append("\n\n");
        foreach (var (lang, tokens) in new[] { ("en", i.TokensEn), ("sq", i.TokensSq) })
        {
            sb.Append(lang == "en" ? "# English\n\n" : "# Shqip\n\n");
            foreach (var key in LandingContent.Keys)
            {
                var p = LandingContent.Get(key, lang);
                string F(string text) => LandingContent.Plain(LandingContent.Fill(text, tokens), i.BaseUrl);
                sb.Append("## ").Append(p.H1).Append('\n');
                sb.Append("URL: ").Append(i.BaseUrl).Append(LandingContent.Localize(p.Path, lang)).Append("\n\n");
                sb.Append(F(p.Lede)).Append("\n\n");
                foreach (var s in p.Sections)
                {
                    var paragraphs = s.Paragraphs.Select(F).Where(x => x.Length > 0).ToList();
                    if (paragraphs.Count == 0) continue;
                    sb.Append("### ").Append(s.Heading).Append("\n\n");
                    foreach (var para in paragraphs) sb.Append(para).Append("\n\n");
                    foreach (var b in s.Bullets ?? Array.Empty<string>()) sb.Append("- ").Append(F(b)).Append('\n');
                    if (s.Bullets != null) sb.Append('\n');
                }
                if (p.Features is { Count: > 0 })
                {
                    sb.Append("### ").Append(p.FeaturesTitle).Append("\n\n");
                    foreach (var f in p.Features) sb.Append("- ").Append(f.Title).Append(": ").Append(F(f.Text)).Append('\n');
                    sb.Append('\n');
                }
                if (p.Comparison is { } table)
                {
                    sb.Append("### ").Append(table.Heading).Append("\n\n");
                    sb.Append("| ").Append(string.Join(" | ", table.Columns)).Append(" |\n");
                    sb.Append('|').Append(string.Concat(table.Columns.Select(_ => " --- |"))).Append('\n');
                    foreach (var row in table.Rows) sb.Append("| ").Append(string.Join(" | ", row.Select(F))).Append(" |\n");
                    sb.Append('\n');
                }
                var faqs = key == "faq" ? LandingContent.FaqGroups(lang).SelectMany(g => g.Faqs) : p.Faqs ?? Array.Empty<LandingContent.Faq>();
                var answered = faqs.Select(f => (Q: F(f.Question), A: F(f.Answer))).Where(f => f.A.Length > 0).ToList();
                if (answered.Count > 0)
                {
                    sb.Append("### ").Append(p.FaqTitle ?? "FAQ").Append("\n\n");
                    foreach (var (q, a) in answered) sb.Append("Q: ").Append(q).Append("\nA: ").Append(a).Append("\n\n");
                }
            }
        }
        return sb.ToString();
    }
}
