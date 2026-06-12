using System.Text.Json;
using BrokerCore.Services;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace BrowserWorker.Handlers;

/// <summary>
/// Policy-aware 分級瀏覽器執行（browser.navigate）。
///
/// 與只做匿名 read 的 BrowserReadHandler 不同，本 handler 消費 broker 附在
/// payload 上的 policy context（max_action_level、intended_action_level、
/// requires_human_confirmation_on），在執行任何動作前先過 BrowserActionGate：
///
/// - read     -> 擷取單頁
/// - navigate -> 同源/允許網域內唯讀多步導覽
/// - authenticate / draft_action / committed_action
///            -> 一律不執行，回傳 gated 結果（需人工確認或超過授權上限）
///
/// 缺少 policy 時上限保守視為 read。runtime 絕不悄悄從 read 升級為 delegated action。
/// </summary>
public sealed class GovernedBrowserActionHandler : ICapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public string CapabilityId => "browser.navigate";

    private readonly IBrowserPageFetcher _fetcher;
    private readonly ILogger<GovernedBrowserActionHandler> _logger;

    public GovernedBrowserActionHandler(IBrowserPageFetcher fetcher, ILogger<GovernedBrowserActionHandler> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        BrowserActionRequest request;
        try
        {
            request = BrowserActionRequest.Parse(requestId, payload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse browser.navigate payload");
            return (false, null, "invalid payload json.");
        }

        if (string.IsNullOrWhiteSpace(request.StartUrl))
            return (false, null, "start_url is required.");

        if (!Uri.TryCreate(request.StartUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, null, "start_url must be an absolute http or https URL.");
        }

        var decision = BrowserActionGate.Evaluate(
            request.IntendedActionLevel,
            request.MaxActionLevel,
            request.RequiresHumanConfirmationOn);

        if (!decision.IsAllowed)
        {
            // 被閘控：不執行任何瀏覽器動作，回傳結構化的 gated 結果。
            _logger.LogInformation(
                "browser.navigate gated for {RequestId}: {Kind} ({Reason})",
                requestId, decision.Kind, decision.Reason);
            return (true, Serialize(BrowserActionExecutionResult.Gated(requestId, decision)), null);
        }

        if (decision.IntendedLevel == BrowserActionLevel.Read)
        {
            var page = await _fetcher.FetchPageAsync(request.StartUrl, request.UserAgent, ct);
            if (!page.Success)
                return (false, null, page.Error ?? "browser_read_failed");
            return (true, Serialize(BrowserActionExecutionResult.FromRead(requestId, decision, page)), null);
        }

        // Navigate level.
        var navigation = await _fetcher.NavigateAsync(
            request.StartUrl,
            request.MaxSteps,
            request.AllowedHostSuffixes,
            request.UserAgent,
            ct);
        if (!navigation.Success)
            return (false, null, navigation.Error ?? "browser_navigate_failed");

        return (true, Serialize(BrowserActionExecutionResult.FromNavigate(requestId, decision, navigation)), null);
    }

    private static string Serialize(BrowserActionExecutionResult result)
        => JsonSerializer.Serialize(result, JsonOptions);
}

/// <summary>browser.navigate 請求（對齊 broker BrowserExecutionRequest 的子集）。</summary>
public sealed class BrowserActionRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string StartUrl { get; set; } = string.Empty;
    public string IntendedActionLevel { get; set; } = string.Empty;
    public string MaxActionLevel { get; set; } = string.Empty;
    public string[] RequiresHumanConfirmationOn { get; set; } = Array.Empty<string>();
    public int MaxSteps { get; set; } = 1;
    public string[] AllowedHostSuffixes { get; set; } = Array.Empty<string>();
    public string? UserAgent { get; set; }

    public static BrowserActionRequest Parse(string requestId, string payload)
    {
        var request = new BrowserActionRequest { RequestId = requestId };
        if (string.IsNullOrWhiteSpace(payload))
            return request;

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var args = root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object
            ? argsEl
            : root;

        request.StartUrl = GetString(root, "start_url")
            ?? GetString(args, "start_url")
            ?? GetString(args, "url")
            ?? string.Empty;
        request.IntendedActionLevel = GetString(root, "intended_action_level") ?? string.Empty;
        request.MaxActionLevel = GetString(root, "max_action_level") ?? string.Empty;
        request.RequiresHumanConfirmationOn = GetStringArray(root, "requires_human_confirmation_on");
        request.UserAgent = GetString(args, "user_agent");

        if (TryGetInt(args, "max_steps", out var steps))
            request.MaxSteps = Math.Clamp(steps, 0, 5);
        request.AllowedHostSuffixes = GetStringArray(args, "allowed_host_suffixes");

        return request;
    }

    private static string? GetString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        return el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.TryGetInt32(out value);
    }

    private static string[] GetStringArray(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return v.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToArray();
    }
}

/// <summary>browser.navigate 結果（對齊 BrowserRuntimeContract 的 BrowserExecutionResult）。</summary>
public sealed class BrowserActionExecutionResult
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Outcome { get; set; } = string.Empty; // executed | gated
    public string ActionLevelReached { get; set; } = string.Empty;
    public string GateDecision { get; set; } = string.Empty;
    public string GateReason { get; set; } = string.Empty;
    public bool RequiresHumanConfirmation { get; set; }
    public string FinalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentText { get; set; } = string.Empty;
    public List<BrowserActionStep> Steps { get; set; } = new();

    public static BrowserActionExecutionResult Gated(string requestId, BrowserActionDecision decision) => new()
    {
        RequestId = requestId,
        Success = true,
        Outcome = "gated",
        ActionLevelReached = BrowserActionLevels.ToWire(BrowserActionLevel.Read),
        GateDecision = decision.Kind.ToString(),
        GateReason = decision.Reason,
        RequiresHumanConfirmation = decision.Kind == BrowserActionDecisionKind.RequiresHumanConfirmation
    };

    public static BrowserActionExecutionResult FromRead(string requestId, BrowserActionDecision decision, BrowserPageResult page) => new()
    {
        RequestId = requestId,
        Success = true,
        Outcome = "executed",
        ActionLevelReached = BrowserActionLevels.ToWire(BrowserActionLevel.Read),
        GateDecision = decision.Kind.ToString(),
        GateReason = decision.Reason,
        FinalUrl = page.FinalUrl,
        Title = page.Title,
        ContentText = page.TextContent,
        Steps = { new BrowserActionStep { FinalUrl = page.FinalUrl, Title = page.Title, StatusCode = page.StatusCode } }
    };

    public static BrowserActionExecutionResult FromNavigate(string requestId, BrowserActionDecision decision, BrowserNavigationResult navigation)
    {
        var last = navigation.Steps.Count > 0 ? navigation.Steps[^1] : null;
        return new BrowserActionExecutionResult
        {
            RequestId = requestId,
            Success = true,
            Outcome = "executed",
            ActionLevelReached = BrowserActionLevels.ToWire(BrowserActionLevel.Navigate),
            GateDecision = decision.Kind.ToString(),
            GateReason = decision.Reason,
            FinalUrl = last?.FinalUrl ?? string.Empty,
            Title = last?.Title ?? string.Empty,
            ContentText = last?.TextContent ?? string.Empty,
            Steps = navigation.Steps.Select(s => new BrowserActionStep
            {
                FinalUrl = s.FinalUrl,
                Title = s.Title,
                StatusCode = s.StatusCode
            }).ToList()
        };
    }
}

public sealed class BrowserActionStep
{
    public string FinalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int StatusCode { get; set; }
}
