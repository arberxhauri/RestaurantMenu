using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RestaurantMenu.Helpers;

/// <summary>
/// The look of every email: a plain, branded card with one button, plus the plain-text
/// version. Everything passed in is plain text and is encoded here, so a name like
/// "&lt;script&gt;" can't inject markup. Table layout and inline styles because email
/// clients (Outlook, Gmail) ignore most CSS.
/// </summary>
public static class EmailTemplate
{
    // Brand orange dark enough for white button text (5.6:1).
    private const string Button = "#A4501F";
    private const string Ink = "#1F1F1F";
    private const string Muted = "#6B6B6B";
    private const string Page = "#F4F4F2";

    /// <param name="preheader">The grey preview line inboxes show after the subject.</param>
    /// <param name="paragraphs">Plain text, one entry per paragraph.</param>
    /// <param name="footnote">Small print under the button, e.g. how long the link works.</param>
    public static (string Html, string Text) Render(string greetingName, string preheader, IEnumerable<string> paragraphs,
        string? buttonText = null, string? link = null, string? footnote = null)
    {
        var paras = paragraphs.ToList();
        string E(string s) => WebUtility.HtmlEncode(s);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
            .Append("<title>My Quick Menu</title></head>")
            .Append($"<body style=\"margin:0;padding:0;background:{Page};\">")
            .Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{E(preheader)}</div>")
            .Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Page};\"><tr><td align=\"center\" style=\"padding:32px 16px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:520px;\">")
            .Append($"<tr><td style=\"padding:0 4px 16px;font:700 18px/1.2 -apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};\">myquick<span style=\"color:{Button};\">menu</span></td></tr>")
            .Append($"<tr><td style=\"background:#FFFFFF;border-radius:12px;padding:32px 28px;font:16px/1.55 -apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};\">")
            .Append($"<p style=\"margin:0 0 16px;\">Hi {E(greetingName)},</p>");
        foreach (var p in paras) html.Append($"<p style=\"margin:0 0 16px;\">{E(p)}</p>");

        if (buttonText != null && link != null)
        {
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:24px 0;\"><tr>")
                .Append($"<td style=\"border-radius:8px;background:{Button};\">")
                .Append($"<a href=\"{E(link)}\" style=\"display:inline-block;padding:13px 24px;border-radius:8px;font-weight:600;color:#FFFFFF;text-decoration:none;\">{E(buttonText)}</a>")
                .Append("</td></tr></table>")
                .Append($"<p style=\"margin:0 0 8px;font-size:13px;color:{Muted};\">If the button doesn't work, copy this link into your browser:</p>")
                .Append($"<p style=\"margin:0 0 16px;font-size:13px;word-break:break-all;\"><a href=\"{E(link)}\" style=\"color:{Button};\">{E(link)}</a></p>");
        }
        if (footnote != null) html.Append($"<p style=\"margin:16px 0 0;font-size:13px;color:{Muted};\">{E(footnote)}</p>");

        html.Append("</td></tr>")
            .Append($"<tr><td style=\"padding:16px 4px 0;font:12px/1.5 -apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Muted};\">")
            .Append("Sent by My Quick Menu, digital menus for restaurants. If you weren't expecting this email, you can ignore it.")
            .Append("</td></tr></table></td></tr></table></body></html>");

        var text = new StringBuilder();
        text.Append($"Hi {greetingName},\n\n");
        foreach (var p in paras) text.Append(p).Append("\n\n");
        if (buttonText != null && link != null) text.Append($"{buttonText}:\n{link}\n\n");
        if (footnote != null) text.Append(footnote).Append("\n\n");
        text.Append("-- \nMy Quick Menu. If you weren't expecting this email, you can ignore it.\n");

        return (html.ToString(), text.ToString());
    }

    /// <summary>Rough plain text from HTML, for callers that only have HTML.</summary>
    public static string HtmlToText(string html)
    {
        var s = Regex.Replace(html, @"<(br|/p|/div|/tr|/h\d)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<a [^>]*href=""([^""]*)""[^>]*>(.*?)</a>", "$2 ($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }
}
