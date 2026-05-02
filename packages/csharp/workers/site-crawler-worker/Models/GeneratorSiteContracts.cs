using System.Text.Json.Serialization;

namespace SiteCrawlerWorker.Models;

public sealed class GeneratorSiteDocument
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "site-generator/v1";

    [JsonPropertyName("site")]
    public GeneratorSiteMetadata Site { get; set; } = new();

    [JsonPropertyName("component_library")]
    public ComponentLibraryManifest ComponentLibrary { get; set; } = new();

    [JsonPropertyName("routes")]
    public List<GeneratorRoute> Routes { get; set; } = new();

    [JsonPropertyName("component_requests")]
    public List<ComponentRequest> ComponentRequests { get; set; } = new();
}

public sealed class GeneratorSiteMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("crawl_run_id")]
    public string CrawlRunId { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public GeneratorTheme Theme { get; set; } = new();
}

public sealed class GeneratorTheme
{
    [JsonPropertyName("colors")]
    public Dictionary<string, string> Colors { get; set; } = new();

    [JsonPropertyName("typography")]
    public Dictionary<string, string> Typography { get; set; } = new();
}

public sealed class ComponentLibraryManifest
{
    [JsonPropertyName("library_id")]
    public string LibraryId { get; set; } = "bricks4agent.default";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("components")]
    public List<ComponentDefinition> Components { get; set; } = new();
}

public sealed class ComponentDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("supported_roles")]
    public List<string> SupportedRoles { get; set; } = new();

    [JsonPropertyName("props")]
    public Dictionary<string, string> Props { get; set; } = new();

    [JsonPropertyName("generated")]
    public bool Generated { get; set; }
}

public sealed class GeneratorRoute
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "/";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("root")]
    public ComponentNode Root { get; set; } = new();
}

public sealed class ComponentNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("props")]
    public Dictionary<string, object?> Props { get; set; } = new();

    [JsonPropertyName("children")]
    public List<ComponentNode> Children { get; set; } = new();
}

public sealed class ComponentRequest
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("component_type")]
    public string ComponentType { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("source_page_url")]
    public string SourcePageUrl { get; set; } = string.Empty;

    [JsonPropertyName("source_selector")]
    public string SourceSelector { get; set; } = string.Empty;
}

public sealed class StaticSitePackageOptions
{
    [JsonPropertyName("output_directory")]
    public string OutputDirectory { get; set; } = string.Empty;

    [JsonPropertyName("package_name")]
    public string PackageName { get; set; } = "generated-site";
}

public sealed class StaticSitePackageResult
{
    [JsonPropertyName("output_directory")]
    public string OutputDirectory { get; set; } = string.Empty;

    [JsonPropertyName("entry_point")]
    public string EntryPoint { get; set; } = string.Empty;

    [JsonPropertyName("site_json_path")]
    public string SiteJsonPath { get; set; } = string.Empty;

    [JsonPropertyName("manifest_path")]
    public string ManifestPath { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();
}
