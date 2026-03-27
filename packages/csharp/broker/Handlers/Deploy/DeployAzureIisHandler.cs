using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Deploy;

public sealed class DeployAzureIisHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "deploy_azure_vm_iis";

    private readonly ILogger<DeployAzureIisHandler> _logger;
    private readonly AzureIisDeploymentExecutionService? _azureIisDeploymentExecutionService;

    public DeployAzureIisHandler(
        ILogger<DeployAzureIisHandler> logger,
        AzureIisDeploymentExecutionService? azureIisDeploymentExecutionService = null)
    {
        _logger = logger;
        _azureIisDeploymentExecutionService = azureIisDeploymentExecutionService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        if (_azureIisDeploymentExecutionService == null)
            return ExecutionResult.Fail(request.RequestId, "AzureIisDeploymentExecutionService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        if (!PayloadHelper.IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var targetId = PayloadHelper.TryGetString(args, "target_id") ?? "";
        var projectPath = PayloadHelper.TryGetString(args, "project_path") ?? "";
        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(projectPath))
            return ExecutionResult.Fail(request.RequestId, "target_id and project_path are required.");

        var input = new AzureIisDeploymentBuildInput
        {
            RequestId = request.RequestId,
            CapabilityId = request.CapabilityId,
            Route = request.Route,
            PrincipalId = request.PrincipalId,
            TaskId = request.TaskId,
            SessionId = request.SessionId,
            TargetId = targetId,
            ProjectPath = projectPath,
            Configuration = PayloadHelper.TryGetString(args, "configuration") ?? "Release",
            RuntimeIdentifier = PayloadHelper.TryGetString(args, "runtime_identifier"),
            SelfContained = args.TryGetProperty("self_contained", out var selfContainedProp) && selfContainedProp.ValueKind == JsonValueKind.True,
            CleanupTarget = !args.TryGetProperty("cleanup_target", out var cleanupProp) || cleanupProp.ValueKind == JsonValueKind.True,
            RestartSite = !args.TryGetProperty("restart_site", out var restartProp) || restartProp.ValueKind == JsonValueKind.True,
            ScopeJson = request.Scope
        };

        var dryRun = args.TryGetProperty("dry_run", out var dryRunProp) && dryRunProp.ValueKind == JsonValueKind.True;
        var result = await _azureIisDeploymentExecutionService.ExecuteAsync("deploy.azure-vm-iis", input, dryRun);
        if (!result.Success || result.Result == null)
            return ExecutionResult.Fail(request.RequestId, result.Result?.Message ?? result.Error ?? "deployment_failed");

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(result.Result));
    }
}
