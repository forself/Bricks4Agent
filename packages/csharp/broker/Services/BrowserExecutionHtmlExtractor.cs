using System.Net;
using System.Text.RegularExpressions;

namespace Broker.Services;

internal static class BrowserExecutionHtmlExtractor
{
    private static readonly Regex TitleRegex = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ScriptStyleRegex = new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string ExtractTitle(string html)
    {
        var match = TitleRegex.Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : string.Empty;
    }

    public static string ExtractText(string html, int maxLength = 4000)
    {
        var withoutScripts = ScriptStyleRegex.Replace(html, " ");
        var withoutTags = TagRegex.Replace(withoutScripts, " ");
        var normalized = SpaceRegex.Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
