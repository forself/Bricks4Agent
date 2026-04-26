using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Models;
using BrokerCore.Data;
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

        // GET /api/v1/llm-proxy/trend?bucket=10&count=24 — 時間 bucket 趨勢
        //   給儀表板畫「最近 N 小時的成功/失敗筆數 + 延遲」長條趨勢圖。
        //   bucket 範圍 1-60 分鐘、count 範圍 1-144；預設 10 分鐘 × 24 格 = 4 小時視窗。
        proxy.MapGet("/trend", (LlmProxyMetrics metrics, HttpContext ctx) =>
        {
            var q = ctx.Request.Query;
            var bucket = int.TryParse(q["bucket"].ToString(), out var b) ? b : 10;
            var count  = int.TryParse(q["count"].ToString(),  out var c) ? c : 24;

            var buckets = metrics.Trend(bucket, count);
            var maxCalls = buckets.Max(x => (int?)x.TotalCalls) ?? 0;
            var totalCallsInWindow = buckets.Sum(x => x.TotalCalls);
            var totalFailInWindow  = buckets.Sum(x => x.FailureCalls);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                bucket_minutes = bucket,
                bucket_count = count,
                window_minutes = bucket * count,
                max_calls_in_any_bucket = maxCalls,
                total_calls = totalCallsInWindow,
                total_failures = totalFailInWindow,
                buckets = buckets.Select(x => new
                {
                    bucket_start = x.BucketStart,
                    total_calls = x.TotalCalls,
                    success_calls = x.SuccessCalls,
                    failure_calls = x.FailureCalls,
                    avg_latency_ms = x.AvgLatencyMs,
                    eval_tokens = x.EvalTokens,
                }),
            }));
        });

        // POST /api/v1/llm-proxy/chat — Worker / 內部服務轉送 LLM 呼叫
        //   走 trusted-internal allowlist（不需要 ECDH session），給 strategy-worker
        //   等容器內服務透過 broker 集中呼叫 LLM，所有呼叫被 MeteredLlmProxyService
        //   記到儀表板的 LLM Proxy 分頁。Body 是 OpenAI-compatible 格式：
        //   { model, messages, temperature, max_tokens, tools }
        //   可選欄位 task_id（broker 端會帶 BrokerTask 進 ChatAsync 給 runtime descriptor 用）
        proxy.MapPost("/chat", async (HttpContext ctx, ILlmProxyService llm, BrokerDb db, CancellationToken ct) =>
        {
            if (!llm.IsEnabled)
            {
                return Results.Json(
                    ApiResponseHelper.Error("LlmProxy is disabled (LlmProxy:Enabled=false)", 503),
                    statusCode: 503);
            }

            JsonElement body;
            string? optionalTaskId = null;
            try
            {
                body = RequestBodyHelper.GetBody(ctx);
                if (body.TryGetProperty("task_id", out var tid) && tid.ValueKind == JsonValueKind.String)
                    optionalTaskId = tid.GetString();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Invalid request body: " + ex.Message));
            }

            BrokerTask? task = null;
            if (!string.IsNullOrWhiteSpace(optionalTaskId))
                task = db.Get<BrokerTask>(optionalTaskId);

            try
            {
                var result = await llm.ChatAsync(body, task, ct);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    content = result.Content,
                    tool_calls = result.ToolCalls,
                    thinking = result.Thinking,
                    done = result.Done,
                    model = result.Model,
                    total_duration = result.TotalDuration,
                    eval_count = result.EvalCount,
                }));
            }
            catch (Exception ex)
            {
                return Results.Json(
                    ApiResponseHelper.Error("LLM upstream error: " + ex.Message, 502),
                    statusCode: 502);
            }
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
