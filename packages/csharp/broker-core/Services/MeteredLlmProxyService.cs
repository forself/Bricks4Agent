using System.Diagnostics;
using System.Text.Json;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// LlmProxyService 的觀測裝飾器：每次 ChatAsync 呼叫前後記錄 latency / token / 成敗。
///
/// 為什麼用 decorator 不直接改 LlmProxyService：
///   - 把觀測邏輯跟業務邏輯分開（單一職責）
///   - 觀測層可以選擇性註冊（DI 開關），不開時零成本
///   - 不污染 LlmProxyService 既有測試
/// </summary>
public class MeteredLlmProxyService : ILlmProxyService
{
    private readonly ILlmProxyService _inner;
    private readonly LlmProxyMetrics _metrics;

    public MeteredLlmProxyService(ILlmProxyService inner, LlmProxyMetrics metrics)
    {
        _inner = inner;
        _metrics = metrics;
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
        var sw = Stopwatch.StartNew();
        var record = new LlmCallRecord
        {
            Ts = DateTime.UtcNow,
            TaskId = task?.TaskId ?? "",
            TaskType = task?.TaskType ?? "",
        };

        try
        {
            var result = await _inner.ChatAsync(body, task, cancellationToken);
            sw.Stop();
            record.Model = result.Model;
            record.Success = true;
            record.LatencyMs = sw.ElapsedMilliseconds;
            record.EvalTokens = result.EvalCount;
            _metrics.Record(record);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            record.Success = false;
            record.LatencyMs = sw.ElapsedMilliseconds;
            // model 可能在 body 裡指定
            if (body.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                record.Model = m.GetString() ?? "";
            var msg = ex.Message ?? ex.GetType().Name;
            record.ErrorBrief = msg.Length > 240 ? msg[..240] : msg;
            _metrics.Record(record);
            throw;
        }
    }
}
