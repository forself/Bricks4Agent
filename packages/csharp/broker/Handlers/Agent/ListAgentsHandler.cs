using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Agent;

public sealed class ListAgentsHandler : IRouteHandler
{
    private readonly AgentSpawnService _agentSpawnService;

    public string Route => "list_agents";

    public ListAgentsHandler(AgentSpawnService agentSpawnService)
    {
        _agentSpawnService = agentSpawnService;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var filter = PayloadHelper.TryGetString(args, "filter");

        var agents = _agentSpawnService.ListAgents();
        if (!string.IsNullOrEmpty(filter))
            agents = agents.Where(a => a.State.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var summary = agents.Select(a => new
        {
            agent_id = a.AgentId,
            state = a.State,
            role = a.RoleId,
            capabilities = a.Capabilities,
            capability_count = a.CapabilityCount,
            created_at = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { agents = summary, total = agents.Count })));
    }
}
