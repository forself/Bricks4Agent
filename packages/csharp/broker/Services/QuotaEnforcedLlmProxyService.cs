using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// H1 — ILlmProxyService decorator：每次 ChatAsync 後把 EvalCount 累計進 quota service。
///
/// 包在 Benson 的 MeteredLlmProxyService 外層、LLM 呼叫 → metered → quota → real LLM。
/// 不改 broker-core 的 LLM 任何一行。
///
/// principal 來源：task.AssignedPrincipalId（可能 null）→ fallback "system"。
/// soft mode（預設）：超 quota 只 log + 寫 audit、不拒呼叫；
/// enforce mode（appsettings Quota:Enforce=true）：超 quota 直接 throw、broker 上層 endpoint 收 500。
/// </summary>
public class QuotaEnforcedLlmProxyService : ILlmProxyService
{
    private readonly ILlmProxyService _inner;
    private readonly IPrincipalQuotaService _quota;
    private readonly ILogger<QuotaEnforcedLlmProxyService> _logger;

    public QuotaEnforcedLlmProxyService(ILlmProxyService inner,
        IPrincipalQuotaService quota,
        ILogger<QuotaEnforcedLlmProxyService> logger)
    {
        _inner = inner; _quota = quota; _logger = logger;
    }

    public bool IsEnabled => _inner.IsEnabled;

    public AgentRuntimeSpec BuildRuntimeSpec(BrokerTask? task = null, IEnumerable<string>? capabilityIds = null)
        => _inner.BuildRuntimeSpec(task, capabilityIds);

    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        => _inner.HealthCheckAsync(cancellationToken);

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => _inner.ListModelsAsync(cancellationToken);

    public async Task<LlmChatResult> ChatAsync(JsonElement body, BrokerTask? task = null, CancellationToken cancellationToken = default)
    {
        var pid = task?.AssignedPrincipalId ?? "system";
        var result = await _inner.ChatAsync(body, task, cancellationToken);

        try
        {
            var (allowed, current, limit) = _quota.RecordLlmUsage(pid, result.EvalCount);
            if (!allowed && _quota.EnforceMode)
            {
                throw new InvalidOperationException(
                    $"LLM token quota exceeded for principal '{pid}': {current}/{limit} (UTC day). " +
                    "Increase quota or set Quota:Enforce=false to soft-mode.");
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quota record failed (non-fatal)");
        }

        return result;
    }
}
