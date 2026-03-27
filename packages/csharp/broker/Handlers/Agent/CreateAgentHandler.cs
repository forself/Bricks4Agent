using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Models;
using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Handlers.Agent;

public sealed class CreateAgentHandler : BrokerCore.Services.IRouteHandler
{
    private readonly AgentSpawnService _agentSpawnService;

    public string Route => "create_agent";

    public CreateAgentHandler(AgentSpawnService agentSpawnService)
    {
        _agentSpawnService = agentSpawnService;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);

        var capabilityIds = new List<string>();
        if (args.TryGetProperty("capability_ids", out var capsEl) && capsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in capsEl.EnumerateArray())
            {
                var id = item.GetString();
                if (!string.IsNullOrEmpty(id)) capabilityIds.Add(id);
            }
        }

        if (capabilityIds.Count == 0)
            capabilityIds = _agentSpawnService.GetDefaultCapabilities();

        var spawnRequest = new AgentSpawnRequest
        {
            DisplayName = PayloadHelper.TryGetString(args, "display_name"),
            CapabilityIds = capabilityIds,
            TaskType = PayloadHelper.TryGetString(args, "task_type") ?? "analysis",
            RequestedBy = request.PrincipalId ?? "agent"
        };

        var result = _agentSpawnService.CreateAgent(spawnRequest);
        if (!result.Success)
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, result.Error ?? "Failed to create agent"));

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new
            {
                agent_id = result.AgentId,
                principal_id = result.PrincipalId,
                task_id = result.TaskId,
                role_id = result.RoleId,
                granted_capabilities = result.GrantedCapabilities,
                max_risk_level = result.MaxRiskLevel,
                warnings = result.Warnings
            })));
    }
}
