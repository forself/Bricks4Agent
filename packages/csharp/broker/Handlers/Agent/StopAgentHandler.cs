using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Agent;

public sealed class StopAgentHandler : IRouteHandler
{
    private readonly AgentSpawnService _agentSpawnService;

    public string Route => "stop_agent";

    public StopAgentHandler(AgentSpawnService agentSpawnService)
    {
        _agentSpawnService = agentSpawnService;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var agentId = PayloadHelper.TryGetString(args, "agent_id") ?? "";

        if (string.IsNullOrEmpty(agentId))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "agent_id is required."));

        var deactivated = _agentSpawnService.DeactivateAgent(agentId);
        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new
            {
                agent_id = agentId,
                deactivated,
                status = deactivated ? "stopped" : "not_found"
            })));
    }
}
