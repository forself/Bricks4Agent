using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public interface IToolSpecRegistry
{
    IReadOnlyList<ToolSpecView> List(string? filter = null);
    ToolSpecView? Get(string toolId);
    IReadOnlyList<ToolSpecDocument> GetDefinitions();
}

public sealed class ToolSpecRegistryOptions
{
    public string Root { get; set; } = "tool-specs";
}

public sealed class ToolSpecRegistry : IToolSpecRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly ILogger<ToolSpecRegistry> _logger;
    private readonly BrokerDb _db;
    private readonly IReadOnlyList<ToolSpecDocument> _definitions;

    public ToolSpecRegistry(
        IWebHostEnvironment environment,
        ToolSpecRegistryOptions options,
        BrokerDb db,
        ILogger<ToolSpecRegistry> logger)
    {
        _logger = logger;
        _db = db;
        var root = ResolveRoot(environment.ContentRootPath, options.Root);
        _definitions = LoadSpecs(root);
        _logger.LogInformation("Tool spec registry loaded {Count} tool specs from {Root}", _definitions.Count, root);
    }

    public IReadOnlyList<ToolSpecView> List(string? filter = null)
    {
        var definitions = _definitions;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            definitions = definitions
                .Where(spec =>
                    spec.ToolId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    spec.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    spec.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    spec.Tags.Any(tag => tag.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        return definitions
            .Select(ToView)
            .ToArray();
    }

    public ToolSpecView? Get(string toolId)
        => _definitions
            .Where(spec => string.Equals(spec.ToolId, toolId, StringComparison.OrdinalIgnoreCase))
            .Select(ToView)
            .FirstOrDefault();

    public IReadOnlyList<ToolSpecDocument> GetDefinitions()
        => _definitions;

    private IReadOnlyList<ToolSpecDocument> LoadSpecs(string root)
    {
        if (!Directory.Exists(root))
        {
            _logger.LogWarning("Tool spec root not found: {Root}", root);
            return Array.Empty<ToolSpecDocument>();
        }

        var specs = new List<ToolSpecDocument>();
        foreach (var toolJsonPath in Directory.EnumerateFiles(root, "tool.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(toolJsonPath);
                var spec = JsonSerializer.Deserialize<ToolSpecFile>(json, JsonOptions);
                if (spec == null || string.IsNullOrWhiteSpace(spec.ToolId))
                {
                    _logger.LogWarning("Skipping invalid tool spec without tool_id: {Path}", toolJsonPath);
                    continue;
                }

                var docPath = Path.Combine(Path.GetDirectoryName(toolJsonPath)!, "TOOL.md");
                specs.Add(ToDocument(spec, toolJsonPath, docPath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tool spec: {Path}", toolJsonPath);
            }
        }

        return specs
            .OrderBy(spec => spec.ToolId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ToolSpecDocument ToDocument(ToolSpecFile spec, string toolJsonPath, string docPath)
    {
        return new ToolSpecDocument
        {
            ToolId = spec.ToolId,
            DisplayName = spec.DisplayName ?? spec.ToolId,
            Summary = spec.Summary ?? string.Empty,
            Kind = spec.Kind ?? string.Empty,
            Status = spec.Status ?? "draft",
            Version = spec.Version ?? string.Empty,
            Tags = spec.Tags ?? Array.Empty<string>(),
            Sources = spec.SourcePolicy?.AllowedSources ?? Array.Empty<string>(),
            InputSchema = spec.InputSchema,
            OutputSchema = spec.OutputSchema,
            ResponseContract = spec.ResponseContract,
            BrowserProfile = spec.BrowserProfile,
            BrowserSessionPolicy = spec.BrowserSessionPolicy,
            BrowserSitePolicy = spec.BrowserSitePolicy,
            ExecutionRules = spec.ExecutionRules,
            CapabilityTemplate = spec.CapabilityTemplate ?? new ToolCapabilityTemplateFile(),
            CapabilityBindings = spec.CapabilityBindings,
            DocMarkdown = File.Exists(docPath) ? File.ReadAllText(docPath) : string.Empty,
            ToolJsonPath = toolJsonPath,
            ToolDocPath = File.Exists(docPath) ? docPath : string.Empty
        };
    }

    private ToolSpecView ToView(ToolSpecDocument spec)
    {
        var bindings = spec.CapabilityBindings
            .Select(binding =>
            {
                var capability = _db.Get<Capability>(binding.CapabilityId);
                return new ToolCapabilityBindingView
                {
                    CapabilityId = binding.CapabilityId,
                    Route = binding.Route,
                    Purpose = binding.Purpose,
                    Registered = capability != null,
                    RegisteredRoute = capability?.Route ?? string.Empty
                };
            })
            .ToArray();

        return new ToolSpecView
        {
            ToolId = spec.ToolId,
            DisplayName = spec.DisplayName ?? spec.ToolId,
            Summary = spec.Summary ?? string.Empty,
            Kind = spec.Kind ?? string.Empty,
            Status = spec.Status ?? "draft",
            Version = spec.Version ?? string.Empty,
            Tags = spec.Tags ?? Array.Empty<string>(),
            Sources = spec.Sources,
            InputSchema = spec.InputSchema,
            OutputSchema = spec.OutputSchema,
            ResponseContract = spec.ResponseContract,
            BrowserProfile = spec.BrowserProfile == null
                ? null
                : new BrowserToolProfileView
                {
                    IdentityMode = spec.BrowserProfile.IdentityMode,
                    CredentialSource = spec.BrowserProfile.CredentialSource,
                    SessionOwner = spec.BrowserProfile.SessionOwner,
                    AllowedActions = spec.BrowserProfile.AllowedActions,
                    ConfirmationPolicy = spec.BrowserProfile.ConfirmationPolicy
                },
            BrowserSessionPolicy = spec.BrowserSessionPolicy == null
                ? null
                : new BrowserSessionPolicyView
                {
                    BindingMode = spec.BrowserSessionPolicy.BindingMode,
                    CredentialBinding = spec.BrowserSessionPolicy.CredentialBinding,
                    ReuseScope = spec.BrowserSessionPolicy.ReuseScope,
                    LeaseMinutes = spec.BrowserSessionPolicy.LeaseMinutes,
                    RequiresConsentRecord = spec.BrowserSessionPolicy.RequiresConsentRecord,
                    RequiresInteractiveLogin = spec.BrowserSessionPolicy.RequiresInteractiveLogin
                },
            BrowserSitePolicy = spec.BrowserSitePolicy == null
                ? null
                : new BrowserSitePolicyView
                {
                    SiteBindingMode = spec.BrowserSitePolicy.SiteBindingMode,
                    AllowedSiteClasses = spec.BrowserSitePolicy.AllowedSiteClasses,
                    RequiresRegisteredSiteBinding = spec.BrowserSitePolicy.RequiresRegisteredSiteBinding,
                    RequiresExactOriginMatch = spec.BrowserSitePolicy.RequiresExactOriginMatch,
                    AllowsCrossOriginNavigation = spec.BrowserSitePolicy.AllowsCrossOriginNavigation
                },
            ExecutionRules = spec.ExecutionRules,
            CapabilityBindings = bindings,
            DocMarkdown = spec.DocMarkdown,
            ToolJsonPath = spec.ToolJsonPath,
            ToolDocPath = spec.ToolDocPath
        };
    }

    private static string ResolveRoot(string contentRootPath, string configuredRoot)
    {
        if (Path.IsPathRooted(configuredRoot))
            return configuredRoot;

        return Path.GetFullPath(Path.Combine(contentRootPath, configuredRoot));
    }
}

public sealed class ToolSpecFile
{
    [JsonPropertyName("tool_id")]
    public string ToolId { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("capability_bindings")]
    public ToolCapabilityBindingFile[] CapabilityBindings { get; set; } = Array.Empty<ToolCapabilityBindingFile>();

    [JsonPropertyName("capability_template")]
    public ToolCapabilityTemplateFile? CapabilityTemplate { get; set; }

    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; set; }

    [JsonPropertyName("output_schema")]
    public JsonElement OutputSchema { get; set; }

    [JsonPropertyName("source_policy")]
    public ToolSourcePolicyFile? SourcePolicy { get; set; }

    [JsonPropertyName("execution_rules")]
    public JsonElement ExecutionRules { get; set; }

    [JsonPropertyName("response_contract")]
    public JsonElement ResponseContract { get; set; }

    [JsonPropertyName("browser_profile")]
    public BrowserToolProfileFile? BrowserProfile { get; set; }

    [JsonPropertyName("browser_session_policy")]
    public BrowserSessionPolicyFile? BrowserSessionPolicy { get; set; }

    [JsonPropertyName("browser_site_policy")]
    public BrowserSitePolicyFile? BrowserSitePolicy { get; set; }
}

public sealed class ToolCapabilityBindingFile
{
    [JsonPropertyName("capability_id")]
    public string CapabilityId { get; set; } = string.Empty;

    [JsonPropertyName("route")]
    public string Route { get; set; } = string.Empty;

    [JsonPropertyName("purpose")]
    public string Purpose { get; set; } = string.Empty;
}

public sealed class ToolSourcePolicyFile
{
    [JsonPropertyName("allowed_sources")]
    public string[] AllowedSources { get; set; } = Array.Empty<string>();
}

public sealed class ToolCapabilityTemplateFile
{
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "read";

    [JsonPropertyName("resource_type")]
    public string ResourceType { get; set; } = "tool";

    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; set; } = "low";

    [JsonPropertyName("approval_policy")]
    public string ApprovalPolicy { get; set; } = "auto";

    [JsonPropertyName("ttl_seconds")]
    public int TtlSeconds { get; set; } = 900;

    [JsonPropertyName("audit_level")]
    public string AuditLevel { get; set; } = "summary";

    [JsonPropertyName("quota")]
    public JsonElement Quota { get; set; }
}

public sealed class BrowserToolProfileFile
{
    [JsonPropertyName("identity_mode")]
    public string IdentityMode { get; set; } = string.Empty;

    [JsonPropertyName("credential_source")]
    public string CredentialSource { get; set; } = string.Empty;

    [JsonPropertyName("session_owner")]
    public string SessionOwner { get; set; } = string.Empty;

    [JsonPropertyName("allowed_actions")]
    public string[] AllowedActions { get; set; } = Array.Empty<string>();

    [JsonPropertyName("confirmation_policy")]
    public string ConfirmationPolicy { get; set; } = string.Empty;
}

public sealed class BrowserSessionPolicyFile
{
    [JsonPropertyName("binding_mode")]
    public string BindingMode { get; set; } = string.Empty;

    [JsonPropertyName("credential_binding")]
    public string CredentialBinding { get; set; } = string.Empty;

    [JsonPropertyName("reuse_scope")]
    public string ReuseScope { get; set; } = string.Empty;

    [JsonPropertyName("lease_minutes")]
    public int LeaseMinutes { get; set; }

    [JsonPropertyName("requires_consent_record")]
    public bool RequiresConsentRecord { get; set; }

    [JsonPropertyName("requires_interactive_login")]
    public bool RequiresInteractiveLogin { get; set; }
}

public sealed class BrowserSitePolicyFile
{
    [JsonPropertyName("site_binding_mode")]
    public string SiteBindingMode { get; set; } = string.Empty;

    [JsonPropertyName("allowed_site_classes")]
    public string[] AllowedSiteClasses { get; set; } = Array.Empty<string>();

    [JsonPropertyName("requires_registered_site_binding")]
    public bool RequiresRegisteredSiteBinding { get; set; }

    [JsonPropertyName("requires_exact_origin_match")]
    public bool RequiresExactOriginMatch { get; set; }

    [JsonPropertyName("allows_cross_origin_navigation")]
    public bool AllowsCrossOriginNavigation { get; set; }
}

public sealed class ToolSpecDocument
{
    public string ToolId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Sources { get; set; } = Array.Empty<string>();
    public JsonElement InputSchema { get; set; }
    public JsonElement OutputSchema { get; set; }
    public JsonElement ExecutionRules { get; set; }
    public JsonElement ResponseContract { get; set; }
    public BrowserToolProfileFile? BrowserProfile { get; set; }
    public BrowserSessionPolicyFile? BrowserSessionPolicy { get; set; }
    public BrowserSitePolicyFile? BrowserSitePolicy { get; set; }
    public ToolCapabilityTemplateFile CapabilityTemplate { get; set; } = new();
    public ToolCapabilityBindingFile[] CapabilityBindings { get; set; } = Array.Empty<ToolCapabilityBindingFile>();
    public string DocMarkdown { get; set; } = string.Empty;
    public string ToolJsonPath { get; set; } = string.Empty;
    public string ToolDocPath { get; set; } = string.Empty;
}

public sealed class ToolSpecView
{
    public string ToolId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Sources { get; set; } = Array.Empty<string>();
    public JsonElement InputSchema { get; set; }
    public JsonElement OutputSchema { get; set; }
    public JsonElement ExecutionRules { get; set; }
    public JsonElement ResponseContract { get; set; }
    public BrowserToolProfileView? BrowserProfile { get; set; }
    public BrowserSessionPolicyView? BrowserSessionPolicy { get; set; }
    public BrowserSitePolicyView? BrowserSitePolicy { get; set; }
    public ToolCapabilityBindingView[] CapabilityBindings { get; set; } = Array.Empty<ToolCapabilityBindingView>();
    public string DocMarkdown { get; set; } = string.Empty;
    public string ToolJsonPath { get; set; } = string.Empty;
    public string ToolDocPath { get; set; } = string.Empty;
}

public sealed class BrowserToolProfileView
{
    public string IdentityMode { get; set; } = string.Empty;
    public string CredentialSource { get; set; } = string.Empty;
    public string SessionOwner { get; set; } = string.Empty;
    public string[] AllowedActions { get; set; } = Array.Empty<string>();
    public string ConfirmationPolicy { get; set; } = string.Empty;
}

public sealed class BrowserSessionPolicyView
{
    public string BindingMode { get; set; } = string.Empty;
    public string CredentialBinding { get; set; } = string.Empty;
    public string ReuseScope { get; set; } = string.Empty;
    public int LeaseMinutes { get; set; }
    public bool RequiresConsentRecord { get; set; }
    public bool RequiresInteractiveLogin { get; set; }
}

public sealed class BrowserSitePolicyView
{
    public string SiteBindingMode { get; set; } = string.Empty;
    public string[] AllowedSiteClasses { get; set; } = Array.Empty<string>();
    public bool RequiresRegisteredSiteBinding { get; set; }
    public bool RequiresExactOriginMatch { get; set; }
    public bool AllowsCrossOriginNavigation { get; set; }
}

public sealed class ToolCapabilityBindingView
{
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public bool Registered { get; set; }
    public string RegisteredRoute { get; set; } = string.Empty;
}
