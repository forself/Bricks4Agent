using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Broker 政策裁決引擎。
///
/// 核心原則：
/// 1. capability 決定「功能層」：這次請求在做什麼。
/// 2. scope 決定「範圍層」：這個 capability 可作用在哪些 route / path。
/// 3. payload 統一採用 { route, args, project_root }，schema 只驗證 args。
/// </summary>
public class PolicyEngine : IPolicyEngine
{
    private readonly ISchemaValidator _schemaValidator;
    private readonly PolicyEngineOptions _options;

    public PolicyEngine(ISchemaValidator schemaValidator, PolicyEngineOptions? options = null)
    {
        _schemaValidator = schemaValidator;
        _options = options ?? new PolicyEngineOptions();
    }

    /// <inheritdoc />
    public PolicyResult Evaluate(
        ExecutionRequest request,
        Capability capability,
        CapabilityGrant grant,
        BrokerTask task,
        int currentEpoch,
        int tokenEpoch)
    {
        if (tokenEpoch < currentEpoch)
        {
            return PolicyResult.Deny(
                $"Token epoch ({tokenEpoch}) is behind system epoch ({currentEpoch}). Token invalidated by kill switch.");
        }

        if (capability.RiskLevel > RiskLevel.Medium)
        {
            return PolicyResult.Deny(
                $"Capability '{capability.CapabilityId}' has risk level {capability.RiskLevel}. Only Low and Medium risk are allowed.");
        }

        var requestedRoute = ExtractRoute(request.RequestPayload) ?? capability.Route;
        if (!string.IsNullOrWhiteSpace(requestedRoute) &&
            !requestedRoute.Equals(capability.Route, StringComparison.OrdinalIgnoreCase))
        {
            return PolicyResult.Deny(
                $"Payload route '{requestedRoute}' does not match capability route '{capability.Route}'.");
        }

        if (!IsScopeValid(request.RequestPayload, requestedRoute, grant.ScopeOverride, task.ScopeDescriptor))
        {
            return PolicyResult.Deny("Request resource is outside the granted scope.");
        }

        if (!IsPathSandboxed(request.RequestPayload))
        {
            return PolicyResult.Deny("File path violates sandbox restrictions.");
        }

        if (ContainsBlacklistedCommand(request.RequestPayload))
        {
            return PolicyResult.Deny("Request contains blacklisted command.");
        }

        var argsPayload = ExtractArgsPayload(request.RequestPayload);
        var (isValid, errorMsg) = _schemaValidator.Validate(argsPayload, capability.ParamSchema);
        if (!isValid)
        {
            return PolicyResult.Deny($"Payload schema validation failed: {errorMsg}");
        }

        return PolicyResult.Allow();
    }

    private static bool IsScopeValid(string payload, string requestedRoute, string grantScope, string taskScope)
    {
        try
        {
            var scopeRoutes = ExtractRoutes(grantScope) ?? ExtractRoutes(taskScope);
            if (scopeRoutes is { Count: > 0 })
            {
                if (string.IsNullOrWhiteSpace(requestedRoute))
                    return false;

                if (!scopeRoutes.Contains(requestedRoute, StringComparer.OrdinalIgnoreCase))
                    return false;
            }

            var scopePaths = ExtractScopePaths(grantScope) ?? ExtractScopePaths(taskScope);
            if (scopePaths is not { Count: > 0 })
                return true;

            var projectRoot = ExtractProjectRoot(payload);
            var requestedPaths = ExtractRequestedPaths(payload, projectRoot);
            if (requestedPaths.Count == 0)
                return true;

            var normalizedAllowed = scopePaths
                .Select(path => NormalizePath(ResolvePath(path, projectRoot) ?? path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return requestedPaths.All(requestPath =>
                normalizedAllowed.Any(allowedPath => IsPathWithin(requestPath, allowedPath)));
        }
        catch
        {
            return true;
        }
    }

    private bool IsPathSandboxed(string payload)
    {
        try
        {
            var projectRoot = ExtractProjectRoot(payload);
            var rawPaths = ExtractRawRequestedPaths(payload);

            foreach (var rawPath in rawPaths)
            {
                var decoded = Uri.UnescapeDataString(rawPath);

                if (decoded.Contains("..", StringComparison.Ordinal) || rawPath.Contains("..", StringComparison.Ordinal))
                    return false;

                if (decoded.Contains('~') || rawPath.Contains('~'))
                    return false;

                if (decoded.Contains('\0') || rawPath.Contains('\0'))
                    return false;

                var normalized = NormalizePath(ResolvePath(decoded, projectRoot) ?? decoded);
                foreach (var prefix in _options.ForbiddenPathPrefixes)
                {
                    if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (normalized.Length == prefix.Length ||
                            normalized[prefix.Length] == '/')
                            return false;
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ContainsBlacklistedCommand(string payload)
    {
        try
        {
            var command = ExtractCommand(payload);
            if (string.IsNullOrWhiteSpace(command))
                return false;

            command = command.Trim();

            foreach (var exact in _options.CommandExactBlacklist)
            {
                if (command.Equals(exact, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var prefix in _options.CommandPrefixBlacklist)
            {
                if (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var sql in _options.SqlBlacklist)
            {
                if (command.Contains(sql, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractArgsPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                return args.GetRawText();
            if (doc.RootElement.TryGetProperty("tool_args", out var legacyArgs) && legacyArgs.ValueKind == JsonValueKind.Object)
                return legacyArgs.GetRawText();
        }
        catch
        {
            // fall through
        }

        return payload;
    }

    private static string? ExtractRoute(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return TryGetString(doc.RootElement, "route", "tool_name");
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractProjectRoot(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return TryGetString(doc.RootElement, "project_root");
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractCommand(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var args = GetArgsElement(doc.RootElement);
            return TryGetString(args, "command");
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ExtractRequestedPaths(string payload, string? projectRoot)
    {
        return ExtractRawRequestedPaths(payload)
            .Select(path => NormalizePath(ResolvePath(path, projectRoot) ?? path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractRawRequestedPaths(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var args = GetArgsElement(doc.RootElement);
            var results = new List<string>();

            AddIfPresent(results, args, "path", "file_path", "directory", "cwd");

            if (results.Count == 0)
                AddIfPresent(results, doc.RootElement, "path", "file_path", "directory", "cwd");

            return results
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void AddIfPresent(List<string> results, JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    results.Add(text);
            }
        }
    }

    private static JsonElement GetArgsElement(JsonElement root)
    {
        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            return args;
        if (root.TryGetProperty("tool_args", out var legacyArgs) && legacyArgs.ValueKind == JsonValueKind.Object)
            return legacyArgs;
        return root;
    }

    private static List<string>? ExtractScopePaths(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
            {
                return paths.EnumerateArray()
                    .Select(path => path.GetString() ?? "")
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToList();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? ExtractRoutes(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("routes", out var routes) && routes.ValueKind == JsonValueKind.Array)
            {
                return routes.EnumerateArray()
                    .Select(route => route.GetString() ?? "")
                    .Where(route => !string.IsNullOrWhiteSpace(route))
                    .ToList();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string path, string? projectRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var decoded = Uri.UnescapeDataString(path);

            if (!string.IsNullOrWhiteSpace(projectRoot) && !Path.IsPathRooted(decoded))
                return Path.GetFullPath(Path.Combine(projectRoot, decoded));

            return Path.GetFullPath(decoded);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPathWithin(string path, string allowedPath)
    {
        if (path.Equals(allowedPath, StringComparison.OrdinalIgnoreCase))
            return true;

        return path.StartsWith(allowedPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static string? TryGetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }
}
