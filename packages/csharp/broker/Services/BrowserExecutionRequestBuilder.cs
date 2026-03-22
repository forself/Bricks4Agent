using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public interface IBrowserExecutionRequestBuilder
{
    BrowserExecutionRequestBuildResult TryBuild(string toolId, BrowserExecutionRequestBuildInput input);
}

public sealed class BrowserExecutionRequestBuilder : IBrowserExecutionRequestBuilder
{
    private static readonly Dictionary<string, int> ActionLevels = new(StringComparer.Ordinal)
    {
        ["read"] = 1,
        ["navigate"] = 2,
        ["authenticate"] = 3,
        ["draft_action"] = 4,
        ["committed_action"] = 5
    };

    private readonly IToolSpecRegistry _registry;
    private readonly BrokerDb _db;

    public BrowserExecutionRequestBuilder(IToolSpecRegistry registry, BrokerDb db)
    {
        _registry = registry;
        _db = db;
    }

    public BrowserExecutionRequestBuildResult TryBuild(string toolId, BrowserExecutionRequestBuildInput input)
    {
        var spec = _registry.Get(toolId);
        if (spec == null)
            return BrowserExecutionRequestBuildResult.Fail("tool_spec_not_found");

        if (!string.Equals(spec.Kind, "browser", StringComparison.OrdinalIgnoreCase))
            return BrowserExecutionRequestBuildResult.Fail("tool_spec_not_browser");

        if (spec.BrowserProfile == null || spec.BrowserSessionPolicy == null || spec.BrowserSitePolicy == null || spec.BrowserActionPolicy == null)
            return BrowserExecutionRequestBuildResult.Fail("browser_tool_spec_incomplete");

        if (string.IsNullOrWhiteSpace(input.RequestId) ||
            string.IsNullOrWhiteSpace(input.CapabilityId) ||
            string.IsNullOrWhiteSpace(input.Route) ||
            string.IsNullOrWhiteSpace(input.PrincipalId) ||
            string.IsNullOrWhiteSpace(input.TaskId) ||
            string.IsNullOrWhiteSpace(input.SessionId) ||
            string.IsNullOrWhiteSpace(input.StartUrl) ||
            string.IsNullOrWhiteSpace(input.IntendedActionLevel))
        {
            return BrowserExecutionRequestBuildResult.Fail("browser_request_input_incomplete");
        }

        if (!ActionLevels.TryGetValue(spec.BrowserActionPolicy.MaxActionLevel, out var maxLevel))
            return BrowserExecutionRequestBuildResult.Fail("browser_tool_invalid_max_action_level");

        if (!ActionLevels.TryGetValue(input.IntendedActionLevel, out var intendedLevel))
            return BrowserExecutionRequestBuildResult.Fail("browser_request_invalid_action_level");

        if (intendedLevel > maxLevel)
            return BrowserExecutionRequestBuildResult.Fail("browser_request_action_level_exceeds_policy");

        if (spec.BrowserSitePolicy.RequiresRegisteredSiteBinding && string.IsNullOrWhiteSpace(input.SiteBindingId))
            return BrowserExecutionRequestBuildResult.Fail("browser_request_missing_site_binding");

        BrowserSiteBinding? siteBinding = null;
        if (!string.IsNullOrWhiteSpace(input.SiteBindingId))
        {
            siteBinding = _db.Get<BrowserSiteBinding>(input.SiteBindingId);
            if (siteBinding == null || !string.Equals(siteBinding.Status, "active", StringComparison.OrdinalIgnoreCase))
                return BrowserExecutionRequestBuildResult.Fail("browser_request_site_binding_not_found");

            if (!string.Equals(siteBinding.IdentityMode, spec.BrowserProfile.IdentityMode, StringComparison.Ordinal))
                return BrowserExecutionRequestBuildResult.Fail("browser_request_site_binding_identity_mismatch");

            if (spec.BrowserSitePolicy.AllowedSiteClasses.Length > 0 &&
                !spec.BrowserSitePolicy.AllowedSiteClasses.Contains(siteBinding.SiteClass, StringComparer.Ordinal))
            {
                return BrowserExecutionRequestBuildResult.Fail("browser_request_site_binding_class_mismatch");
            }
        }

        if (string.Equals(spec.BrowserProfile.IdentityMode, "user_delegated", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(input.UserGrantId))
        {
            return BrowserExecutionRequestBuildResult.Fail("browser_request_missing_user_grant");
        }

        BrowserUserGrant? userGrant = null;
        if (string.Equals(spec.BrowserProfile.IdentityMode, "user_delegated", StringComparison.Ordinal) &&
            siteBinding != null &&
            !string.Equals(siteBinding.PrincipalId, input.PrincipalId, StringComparison.Ordinal))
        {
            return BrowserExecutionRequestBuildResult.Fail("browser_request_site_binding_principal_mismatch");
        }

        if (string.Equals(spec.BrowserProfile.IdentityMode, "system_account", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(input.SystemBindingId))
        {
            return BrowserExecutionRequestBuildResult.Fail("browser_request_missing_system_binding");
        }

        if (!string.IsNullOrWhiteSpace(input.UserGrantId))
        {
            userGrant = _db.Get<BrowserUserGrant>(input.UserGrantId);
            if (userGrant == null || !string.Equals(userGrant.Status, "active", StringComparison.OrdinalIgnoreCase))
                return BrowserExecutionRequestBuildResult.Fail("browser_request_user_grant_not_found");

            if (!string.Equals(userGrant.PrincipalId, input.PrincipalId, StringComparison.Ordinal))
                return BrowserExecutionRequestBuildResult.Fail("browser_request_user_grant_principal_mismatch");

            if (!string.IsNullOrWhiteSpace(userGrant.SiteBindingId) &&
                !string.Equals(userGrant.SiteBindingId, input.SiteBindingId, StringComparison.Ordinal))
            {
                return BrowserExecutionRequestBuildResult.Fail("browser_request_user_grant_site_binding_mismatch");
            }

            if (userGrant.ExpiresAt.HasValue && userGrant.ExpiresAt.Value < DateTime.UtcNow)
                return BrowserExecutionRequestBuildResult.Fail("browser_request_user_grant_expired");
        }

        if (!string.IsNullOrWhiteSpace(input.SystemBindingId))
        {
            var systemBinding = _db.Get<BrowserSystemBinding>(input.SystemBindingId);
            if (systemBinding == null || !string.Equals(systemBinding.Status, "active", StringComparison.OrdinalIgnoreCase))
                return BrowserExecutionRequestBuildResult.Fail("browser_request_system_binding_not_found");

            if (!string.IsNullOrWhiteSpace(systemBinding.SiteBindingId) &&
                !string.Equals(systemBinding.SiteBindingId, input.SiteBindingId, StringComparison.Ordinal))
            {
                return BrowserExecutionRequestBuildResult.Fail("browser_request_system_binding_site_mismatch");
            }
        }

        return BrowserExecutionRequestBuildResult.Ok(new BrowserExecutionRequest
        {
            RequestId = input.RequestId,
            ToolId = spec.ToolId,
            CapabilityId = input.CapabilityId,
            Route = input.Route,
            IdentityMode = spec.BrowserProfile.IdentityMode,
            CredentialBinding = spec.BrowserSessionPolicy.CredentialBinding,
            SessionBindingMode = spec.BrowserSessionPolicy.BindingMode,
            SessionReuseScope = spec.BrowserSessionPolicy.ReuseScope,
            SiteBindingMode = spec.BrowserSitePolicy.SiteBindingMode,
            AllowedSiteClasses = spec.BrowserSitePolicy.AllowedSiteClasses,
            MaxActionLevel = spec.BrowserActionPolicy.MaxActionLevel,
            RequiresHumanConfirmationOn = spec.BrowserActionPolicy.RequiresHumanConfirmationOn,
            PrincipalId = input.PrincipalId,
            TaskId = input.TaskId,
            SessionId = input.SessionId,
            SiteBindingId = input.SiteBindingId,
            UserGrantId = input.UserGrantId,
            SystemBindingId = input.SystemBindingId,
            SessionLeaseId = input.SessionLeaseId,
            StartUrl = input.StartUrl,
            IntendedActionLevel = input.IntendedActionLevel,
            ArgumentsJson = string.IsNullOrWhiteSpace(input.ArgumentsJson) ? "{}" : input.ArgumentsJson,
            ScopeJson = string.IsNullOrWhiteSpace(input.ScopeJson) ? "{}" : input.ScopeJson
        });
    }
}

public sealed class BrowserExecutionRequestBuildInput
{
    public string RequestId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string StartUrl { get; set; } = string.Empty;
    public string IntendedActionLevel { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string ScopeJson { get; set; } = "{}";
    public string? SiteBindingId { get; set; }
    public string? UserGrantId { get; set; }
    public string? SystemBindingId { get; set; }
    public string? SessionLeaseId { get; set; }
}

public sealed class BrowserExecutionRequestBuildResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public BrowserExecutionRequest? Request { get; set; }

    public static BrowserExecutionRequestBuildResult Ok(BrowserExecutionRequest request)
        => new()
        {
            Success = true,
            Request = request
        };

    public static BrowserExecutionRequestBuildResult Fail(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
