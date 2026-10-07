using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrokerCore.Services;

public sealed class TaskRuntimeDescriptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("llm")]
    public TaskLlmDescriptor Llm { get; set; } = new();
    [JsonPropertyName("capability_ids")]
    public List<string> CapabilityIds { get; set; } = [];
    [JsonPropertyName("capability_grants")]
    public List<TaskCapabilityGrantTemplate> CapabilityGrants { get; set; } = [];

    public bool HasCapabilityOverrides => CapabilityIds.Count > 0 || CapabilityGrants.Count > 0;

    public static TaskRuntimeDescriptor Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new TaskRuntimeDescriptor();
        }

        try
        {
            var descriptor = JsonSerializer.Deserialize<TaskRuntimeDescriptor>(raw, JsonOptions)
                ?? new TaskRuntimeDescriptor();
            descriptor.CapabilityIds ??= [];
            descriptor.CapabilityGrants ??= [];
            descriptor.Llm ??= new TaskLlmDescriptor();
            return descriptor;
        }
        catch (JsonException)
        {
            return new TaskRuntimeDescriptor();
        }
    }
}

public sealed class TaskLlmDescriptor
{
    [JsonPropertyName("default_model")]
    public string DefaultModel { get; set; } = string.Empty;
    [JsonPropertyName("allow_model_override")]
    public bool? AllowModelOverride { get; set; }
    [JsonPropertyName("supports_tool_calling")]
    public bool? SupportsToolCalling { get; set; }
    [JsonPropertyName("streaming_enabled")]
    public bool? StreamingEnabled { get; set; }

    public bool HasOverrides =>
        !string.IsNullOrWhiteSpace(DefaultModel) ||
        AllowModelOverride.HasValue ||
        SupportsToolCalling.HasValue ||
        StreamingEnabled.HasValue;
}

public sealed class TaskCapabilityGrantTemplate
{
    [JsonPropertyName("capability_id")]
    public string CapabilityId { get; set; } = string.Empty;
    [JsonPropertyName("scope")]
    public JsonElement Scope { get; set; }
    [JsonPropertyName("quota")]
    public int? Quota { get; set; }

    /// <summary>
    /// 配額的計算範圍：省略或 <c>session</c> 時每個 session 註冊都拿到完整的 <see cref="Quota"/>（既有行為）；
    /// <see cref="TaskQuotaScope"/> 時以任務累計，同一任務的 session 重新註冊（容器重啟、session 過期後再註冊）
    /// 只拿到這個任務尚未用掉的次數。
    /// </summary>
    [JsonPropertyName("quota_scope")]
    public string? QuotaScope { get; set; }

    /// <summary><see cref="QuotaScope"/> 的任務累計值。</summary>
    public const string TaskQuotaScope = "task";

    public bool IsTaskScopedQuota
        => string.Equals(QuotaScope?.Trim(), TaskQuotaScope, StringComparison.OrdinalIgnoreCase);

    public string ResolveScopeOverride(string fallbackScope)
    {
        return Scope.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? fallbackScope
            : Scope.GetRawText();
    }

    public int ResolveQuota(int fallbackQuota = -1)
        => Quota ?? fallbackQuota;
}
