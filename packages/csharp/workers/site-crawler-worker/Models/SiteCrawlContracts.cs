using System.Text.Json.Serialization;

namespace SiteCrawlerWorker.Models;

public sealed class SiteCrawlRequest
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("start_url")]
    public string StartUrl { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public SiteCrawlScope Scope { get; set; } = new();

    [JsonPropertyName("capture")]
    public SiteCrawlCaptureOptions Capture { get; set; } = new();

    [JsonPropertyName("budgets")]
    public SiteCrawlBudgets Budgets { get; set; } = new();
}

public sealed class SiteCrawlScope
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "path_depth";

    [JsonPropertyName("max_depth")]
    public int MaxDepth { get; set; }

    [JsonPropertyName("same_origin_only")]
    public bool SameOriginOnly { get; set; } = true;

    [JsonPropertyName("path_prefix_lock")]
    public bool PathPrefixLock { get; set; } = true;
}

public sealed class SiteCrawlCaptureOptions
{
    [JsonPropertyName("html")]
    public bool Html { get; set; } = true;

    [JsonPropertyName("rendered_dom")]
    public bool RenderedDom { get; set; } = true;

    [JsonPropertyName("css")]
    public bool Css { get; set; } = true;

    [JsonPropertyName("scripts")]
    public bool Scripts { get; set; } = true;

    [JsonPropertyName("assets")]
    public bool Assets { get; set; } = true;

    [JsonPropertyName("screenshots")]
    public bool Screenshots { get; set; }
}

public sealed class SiteCrawlBudgets
{
    [JsonPropertyName("max_pages")]
    public int MaxPages { get; set; } = 50;

    [JsonPropertyName("max_total_bytes")]
    public long MaxTotalBytes { get; set; } = 10 * 1024 * 1024;

    [JsonPropertyName("max_asset_bytes")]
    public long MaxAssetBytes { get; set; } = 2 * 1024 * 1024;

    [JsonPropertyName("wall_clock_timeout_seconds")]
    public int WallClockTimeoutSeconds { get; set; } = 180;
}

public sealed class SiteCrawlResult
{
    [JsonPropertyName("crawl_run_id")]
    public string CrawlRunId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "completed";

    [JsonPropertyName("root")]
    public SiteCrawlRoot Root { get; set; } = new();

    [JsonPropertyName("pages")]
    public List<SiteCrawlPage> Pages { get; set; } = new();

    [JsonPropertyName("assets")]
    public List<SiteCrawlAsset> Assets { get; set; } = new();

    [JsonPropertyName("excluded")]
    public List<SiteCrawlExcludedUrl> Excluded { get; set; } = new();

    [JsonPropertyName("extracted_model")]
    public ExtractedSiteModel ExtractedModel { get; set; } = new();

    [JsonPropertyName("limits")]
    public SiteCrawlLimitState Limits { get; set; } = new();
}

public sealed class SiteCrawlRoot
{
    [JsonPropertyName("start_url")]
    public string StartUrl { get; set; } = string.Empty;

    [JsonPropertyName("normalized_start_url")]
    public string NormalizedStartUrl { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = string.Empty;

    [JsonPropertyName("path_prefix")]
    public string PathPrefix { get; set; } = "/";
}

public sealed class SiteCrawlPage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("final_url")]
    public string FinalUrl { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("html")]
    public string Html { get; set; } = string.Empty;

    [JsonPropertyName("text_excerpt")]
    public string TextExcerpt { get; set; } = string.Empty;

    [JsonPropertyName("links")]
    public List<string> Links { get; set; } = new();

    [JsonPropertyName("forms")]
    public List<ExtractedForm> Forms { get; set; } = new();

    [JsonPropertyName("resources")]
    public List<string> Resources { get; set; } = new();
}

public sealed class SiteCrawlAsset
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SiteCrawlExcludedUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SiteCrawlLimitState
{
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("page_limit_hit")]
    public bool PageLimitHit { get; set; }

    [JsonPropertyName("byte_limit_hit")]
    public bool ByteLimitHit { get; set; }
}

public sealed class ExtractedSiteModel
{
    [JsonPropertyName("pages")]
    public List<ExtractedPageModel> Pages { get; set; } = new();

    [JsonPropertyName("theme_tokens")]
    public ExtractedThemeTokens ThemeTokens { get; set; } = new();

    [JsonPropertyName("route_graph")]
    public ExtractedRouteGraph RouteGraph { get; set; } = new();
}

public sealed class ExtractedPageModel
{
    [JsonPropertyName("page_url")]
    public string PageUrl { get; set; } = string.Empty;

    [JsonPropertyName("sections")]
    public List<ExtractedSection> Sections { get; set; } = new();
}

public sealed class ExtractedSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "content";

    [JsonPropertyName("headline")]
    public string Headline { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("source_selector")]
    public string SourceSelector { get; set; } = string.Empty;

    [JsonPropertyName("media")]
    public List<ExtractedMedia> Media { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<ExtractedAction> Actions { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ExtractedItem> Items { get; set; } = new();
}

public sealed class ExtractedMedia
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "image";

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("alt")]
    public string Alt { get; set; } = string.Empty;
}

public sealed class ExtractedAction
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "secondary";
}

public sealed class ExtractedItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("media_url")]
    public string MediaUrl { get; set; } = string.Empty;

    [JsonPropertyName("media_alt")]
    public string MediaAlt { get; set; } = string.Empty;
}

public sealed class ExtractedForm
{
    [JsonPropertyName("selector")]
    public string Selector { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = "get";

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public List<ExtractedFormField> Fields { get; set; } = new();
}

public sealed class ExtractedFormField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ExtractedThemeTokens
{
    [JsonPropertyName("colors")]
    public Dictionary<string, string> Colors { get; set; } = new();

    [JsonPropertyName("typography")]
    public Dictionary<string, string> Typography { get; set; } = new();
}

public sealed class ExtractedRouteGraph
{
    [JsonPropertyName("routes")]
    public List<ExtractedRoute> Routes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<ExtractedRouteEdge> Edges { get; set; } = new();
}

public sealed class ExtractedRoute
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("page_id")]
    public string PageId { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public sealed class ExtractedRouteEdge
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "internal_link";
}
