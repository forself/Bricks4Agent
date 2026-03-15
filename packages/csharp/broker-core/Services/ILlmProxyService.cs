using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Models;

namespace BrokerCore.Services;

public interface ILlmProxyService
{
    bool IsEnabled { get; }
    AgentRuntimeSpec BuildRuntimeSpec(BrokerTask? task = null, IEnumerable<string>? capabilityIds = null);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<LlmChatResult> ChatAsync(JsonElement body, BrokerTask? task = null, CancellationToken cancellationToken = default);
}

public class AgentRuntimeSpec
{
    public string Source { get; set; } = "broker_default";
    public string Provider { get; set; } = "ollama";
    public string ApiFormat { get; set; } = "chat";
    public string DefaultModel { get; set; } = "llama3.1";
    public bool AllowModelOverride { get; set; }
    public bool SupportsToolCalling { get; set; } = true;
    public bool StreamingEnabled { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string AssignedRoleId { get; set; } = string.Empty;
    public string ScopeDescriptor { get; set; } = "{}";
    public string[] CapabilityIds { get; set; } = Array.Empty<string>();
}

public class LlmModelInfo
{
    public string Name { get; set; } = string.Empty;
    public long? Size { get; set; }
}

public class LlmChatResult
{
    public string Content { get; set; } = string.Empty;
    public JsonArray ToolCalls { get; set; } = [];
    public string Thinking { get; set; } = string.Empty;
    public bool Done { get; set; } = true;
    public string Model { get; set; } = string.Empty;
    public long TotalDuration { get; set; }
    public int EvalCount { get; set; }
}
