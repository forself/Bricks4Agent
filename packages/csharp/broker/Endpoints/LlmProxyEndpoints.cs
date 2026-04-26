using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// LLM 代理觀測端點 — 給儀表板顯示集中式 LLM 治理層的狀態與用量。
///
/// 配對 LlmProxyOptions（config）+ LlmProxyMetrics（runtime stats）。
/// 不洩漏 ApiKey / BaseUrl 完整值。
/// </summary>
public static class LlmProxyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var proxy = group.MapGroup("/llm-proxy");

        // GET /api/v1/llm-proxy/status — 設定 + 累計 metrics
        proxy.MapGet("/status", (
            LlmProxyOptions options,
            LlmProxyMetrics metrics,
            ILlmProxyService llm) =>
        {
            var snap = metrics.Snapshot();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                config = new
                {
                    enabled = options.Enabled,
                    provider = options.Provider,
                    api_format = options.ApiFormat,
                    default_model = options.DefaultModel,
                    base_url_host = SafeHost(options.BaseUrl),
                    api_key_set = !string.IsNullOrWhiteSpace(options.ApiKey),
                    api_key_length = options.ApiKey?.Length ?? 0,
                    allow_model_override = options.AllowModelOverride,
                    supports_tool_calling = options.SupportsToolCalling,
                    streaming_enabled = options.StreamingEnabled,
                    timeout_seconds = options.TimeoutSeconds,
                },
                metrics = new
                {
                    started_at = snap.StartedAt,
                    uptime_seconds = (int)(DateTime.UtcNow - snap.StartedAt).TotalSeconds,
                    total_calls = snap.TotalCalls,
                    success_calls = snap.SuccessCalls,
                    failure_calls = snap.FailureCalls,
                    success_rate_pct = Math.Round(snap.SuccessRatePct, 2),
                    total_eval_tokens = snap.TotalEvalTokens,
                    avg_latency_ms = snap.AvgLatencyMs,
                    per_model = snap.PerModel.Select(m => new
                    {
                        model = m.Model,
                        calls = m.Calls,
                        success_calls = m.SuccessCalls,
                        eval_tokens = m.EvalTokens,
                        avg_latency_ms = m.AvgLatencyMs,
                        last_ts = m.LastTs,
                    }),
                },
            }));
        });

        // GET /api/v1/llm-proxy/recent?limit=50 — 最近 N 筆呼叫
        proxy.MapGet("/recent", (LlmProxyMetrics metrics, HttpContext ctx) =>
        {
            var limitStr = ctx.Request.Query["limit"].ToString();
            var limit = int.TryParse(limitStr, out var l)
                ? Math.Clamp(l, 1, LlmProxyMetrics.MaxRecentEntries)
                : 50;

            var entries = metrics.Recent(limit);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = entries.Count,
                entries = entries.Select(e => new
                {
                    ts = e.Ts,
                    model = e.Model,
                    task_id = e.TaskId,
                    task_type = e.TaskType,
                    success = e.Success,
                    latency_ms = e.LatencyMs,
                    eval_tokens = e.EvalTokens,
                    error_brief = e.ErrorBrief,
                }),
            }));
        });

        // GET /api/v1/llm-proxy/healthcheck — 主動 ping 上游確認可用
        proxy.MapGet("/healthcheck", async (ILlmProxyService llm, CancellationToken ct) =>
        {
            if (!llm.IsEnabled)
            {
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    enabled = false,
                    upstream_reachable = (bool?)null,
                    note = "LlmProxy is disabled (LlmProxy:Enabled=false)",
                }));
            }
            try
            {
                var ok = await llm.HealthCheckAsync(ct);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    enabled = true,
                    upstream_reachable = ok,
                    checked_at = DateTime.UtcNow,
                }));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    enabled = true,
                    upstream_reachable = false,
                    error = ex.Message.Length > 240 ? ex.Message[..240] : ex.Message,
                    checked_at = DateTime.UtcNow,
                }));
            }
        });
    }

    /// <summary>只回傳 host 部分，不洩漏完整 base url（避免 path token / 私有 internal URL 外露）。</summary>
    private static string SafeHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return "";
        try
        {
            var uri = new Uri(baseUrl, UriKind.Absolute);
            return uri.Host;
        }
        catch
        {
            return "(invalid url)";
        }
    }
}
