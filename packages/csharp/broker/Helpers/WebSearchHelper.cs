using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broker.Helpers;

/// <summary>
/// Shared static helpers for web search, HTML parsing, and Wikipedia access.
/// Extracted from InProcessDispatcher to be reused across route handlers.
/// </summary>
public static class WebSearchHelper
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    static WebSearchHelper()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("B4A-Agent/1.0");
    }

    /// <summary>Expose the shared HttpClient for handlers that need direct access.</summary>
    public static HttpClient SharedHttpClient => HttpClient;

    // ── DuckDuckGo ──

    public static async Task<List<object>> SearchDuckDuckGoAsync(string query, int limit, string locale)
    {
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}&kl={Uri.EscapeDataString(locale)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", $"{locale},en;q=0.8");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        return ParseDuckDuckGoLite(html, limit);
    }

    // ── Google ──

    public static async Task<List<object>> SearchGoogleAsync(string query, int limit, string locale, string safeMode)
    {
        var safe = safeMode switch
        {
            "strict" => "active",
            "off" => "off",
            _ => "active"
        };

        var url = $"https://www.google.com/search?gbv=1&q={Uri.EscapeDataString(query)}&num={limit}&hl={Uri.EscapeDataString(locale)}&safe={safe}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", $"{locale},en;q=0.8");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        return ParseGoogleResults(html, limit);
    }

    // ── Wikipedia ──

    public static async Task<List<object>> SearchWikipediaAsync(string query, int limit, string locale)
    {
        foreach (var language in ResolveWikipediaLanguages(locale))
        {
            var results = await SearchWikipediaLanguageAsync(query, limit, language);
            if (results.Count > 0)
                return results;
        }

        return new List<object>();
    }

    public static async Task<List<object>> SearchWikipediaLanguageAsync(string query, int limit, string language)
    {
        var searchUrl =
            $"https://{language}.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&utf8=1&format=json&srlimit={limit}";
        using var response = await HttpClient.GetAsync(searchUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var queryNode) ||
            !queryNode.TryGetProperty("search", out var searchNode) ||
            searchNode.ValueKind != JsonValueKind.Array)
        {
            return new List<object>();
        }

        var pages = new List<(long PageId, string Title, string Snippet)>();
        foreach (var item in searchNode.EnumerateArray())
        {
            if (!item.TryGetProperty("pageid", out var pageIdNode) ||
                !pageIdNode.TryGetInt64(out var pageId))
            {
                continue;
            }

            var title = item.TryGetProperty("title", out var titleNode)
                ? titleNode.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var snippet = item.TryGetProperty("snippet", out var snippetNode)
                ? HtmlToText(snippetNode.GetString() ?? string.Empty)
                : string.Empty;

            pages.Add((pageId, title, snippet));
        }

        if (pages.Count == 0)
            return new List<object>();

        var extracts = await FetchWikipediaExtractsAsync(language, pages.Select(page => page.PageId));
        var results = new List<object>();

        foreach (var page in pages.Take(limit))
        {
            var snippet = extracts.TryGetValue(page.PageId, out var extract) && !string.IsNullOrWhiteSpace(extract)
                ? ShortenText(extract, 900)
                : page.Snippet;
            var pageTitle = page.Title.Replace(' ', '_');
            results.Add(new
            {
                rank = results.Count + 1,
                title = page.Title,
                url = $"https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(pageTitle)}",
                snippet
            });
        }

        return results;
    }

    public static async Task<Dictionary<long, string>> FetchWikipediaExtractsAsync(string language, IEnumerable<long> pageIds)
    {
        var pageIdList = pageIds.Distinct().ToArray();
        if (pageIdList.Length == 0)
            return new Dictionary<long, string>();

        var extractUrl =
            $"https://{language}.wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&exintro=1&format=json&pageids={string.Join("|", pageIdList)}";
        using var response = await HttpClient.GetAsync(extractUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var queryNode) ||
            !queryNode.TryGetProperty("pages", out var pagesNode) ||
            pagesNode.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<long, string>();
        }

        var extracts = new Dictionary<long, string>();
        foreach (var property in pagesNode.EnumerateObject())
        {
            if (!long.TryParse(property.Name, out var pageId))
                continue;

            var extract = property.Value.TryGetProperty("extract", out var extractNode)
                ? NormalizeWikipediaExtract(extractNode.GetString() ?? string.Empty)
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(extract))
                extracts[pageId] = extract;
        }

        return extracts;
    }

    public static IEnumerable<string> ResolveWikipediaLanguages(string locale)
    {
        if (!string.IsNullOrWhiteSpace(locale) &&
            locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            yield return "zh";
            yield return "en";
            yield break;
        }

        yield return "en";
        yield return "zh";
    }

    public static string NormalizeWikipediaExtract(string text)
        => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

    public static string ShortenText(string text, int maxLength)
        => string.IsNullOrWhiteSpace(text) || text.Length <= maxLength
            ? text
            : text[..maxLength].TrimEnd() + "\u2026";

    // ── HTML parsing ──

    public static List<object> ParseDuckDuckGoLite(string html, int limit)
    {
        var results = new List<object>();
        var linkPattern = new Regex(
            @"<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!linkPattern.IsMatch(html))
        {
            linkPattern = new Regex(
                @"<a[^>]+href=""(https?://[^""]+)""[^>]*class=""result-link""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        var snippetPattern = new Regex(
            @"<a[^>]+class=""result__snippet""[^>]*>(.*?)</a>|<td[^>]*class=""result-snippet""[^>]*>(.*?)</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var links = linkPattern.Matches(html);
        var snippets = snippetPattern.Matches(html);

        for (int i = 0; i < Math.Min(links.Count, limit); i++)
        {
            var link = links[i];
            var href = System.Net.WebUtility.HtmlDecode(link.Groups[1].Value);
            if (href.Contains("uddg=", StringComparison.OrdinalIgnoreCase))
            {
                var uddg = Regex.Match(href, @"uddg=([^&]+)", RegexOptions.IgnoreCase);
                if (uddg.Success)
                    href = Uri.UnescapeDataString(uddg.Groups[1].Value);
            }

            var snippet = i < snippets.Count
                ? HtmlToText(snippets[i].Groups[1].Success ? snippets[i].Groups[1].Value : snippets[i].Groups[2].Value).Trim()
                : "";

            results.Add(new
            {
                rank = i + 1,
                url = href,
                title = HtmlToText(link.Groups[2].Value).Trim(),
                snippet
            });
        }

        return results;
    }

    public static List<object> ParseGoogleResults(string html, int limit)
    {
        var results = new List<object>();
        var linkPattern = new Regex(
            @"<a[^>]+href=""/url\?q=([^&""]+)[^""]*""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in linkPattern.Matches(html))
        {
            if (results.Count >= limit)
                break;

            var href = Uri.UnescapeDataString(match.Groups[1].Value);
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                continue;

            var anchorHtml = match.Groups[2].Value;
            var titleMatch = Regex.Match(anchorHtml, @"<h3[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success
                ? HtmlToText(titleMatch.Groups[1].Value).Trim()
                : HtmlToText(anchorHtml).Trim();

            if (string.IsNullOrWhiteSpace(title))
                title = href;

            results.Add(new
            {
                rank = results.Count + 1,
                title,
                url = href,
                snippet = string.Empty
            });
        }

        return results;
    }

    public static string HtmlToText(string html)
    {
        // Remove script/style
        var text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // <br> / <p> / <div> -> newline
        text = Regex.Replace(text, @"<(br|/p|/div|/tr|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        // Remove all HTML tags
        text = Regex.Replace(text, @"<[^>]+>", "");
        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);
        // Compress whitespace
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    public static IEnumerable<string> ExtractTimeCandidates(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (Match match in Regex.Matches(text, @"(?<!\d)([01]?\d|2[0-3]):[0-5]\d(?!\d)"))
            yield return match.Value;
    }
}
