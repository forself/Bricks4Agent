using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Browser;

public sealed class BrowserReadHandler : IRouteHandler
{
    public string Route => "browser_read";

    private readonly ILogger<BrowserReadHandler> _logger;
    private readonly BrowserExecutionRuntimeService? _browserExecutionRuntimeService;

    public BrowserReadHandler(
        ILogger<BrowserReadHandler> logger,
        BrowserExecutionRuntimeService? browserExecutionRuntimeService = null)
    {
        _logger = logger;
        _browserExecutionRuntimeService = browserExecutionRuntimeService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        if (_browserExecutionRuntimeService == null)
            return ExecutionResult.Fail(request.RequestId, "BrowserExecutionRuntimeService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        if (!PayloadHelper.IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var startUrl = PayloadHelper.TryGetString(args, "url", "start_url") ?? string.Empty;
        var intendedActionLevel = PayloadHelper.TryGetString(args, "action_level", "intended_action_level") ?? "read";
        var toolId = PayloadHelper.TryGetString(args, "tool_id") ?? "browser.reference.anonymous.read";
        var scopeJson = doc.RootElement.TryGetProperty("scope", out var scopeElement)
            ? scopeElement.GetRawText()
            : "{}";

        var input = new BrowserExecutionRequestBuildInput
        {
            RequestId = request.RequestId,
            CapabilityId = request.CapabilityId,
            Route = request.Route,
            PrincipalId = request.PrincipalId,
            TaskId = request.TaskId,
            SessionId = request.SessionId,
            StartUrl = startUrl,
            IntendedActionLevel = intendedActionLevel,
            ArgumentsJson = args.GetRawText(),
            ScopeJson = scopeJson,
            SiteBindingId = PayloadHelper.TryGetString(args, "site_binding_id"),
            UserGrantId = PayloadHelper.TryGetString(args, "user_grant_id"),
            SystemBindingId = PayloadHelper.TryGetString(args, "system_binding_id"),
            SessionLeaseId = PayloadHelper.TryGetString(args, "session_lease_id")
        };

        var result = await _browserExecutionRuntimeService.ExecuteAnonymousReadAsync(toolId, input);
        if (!result.Success || result.Result == null)
            return ExecutionResult.Fail(request.RequestId, result.Error ?? "browser_runtime_failed");

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
        {
            tool_id = toolId,
            result = result.Result
        }));
    }
}
