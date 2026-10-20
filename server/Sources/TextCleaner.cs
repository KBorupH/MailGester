using System.Net;
using System.Text.RegularExpressions;

namespace MailGester.Sources;

public static partial class TextCleaner
{
    [GeneratedRegex(@"<(script|style)\b.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string FromHtml(string html) =>
        Collapse(WebUtility.HtmlDecode(Tags().Replace(ScriptOrStyle().Replace(html, " "), " ")));

    public static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    public static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
