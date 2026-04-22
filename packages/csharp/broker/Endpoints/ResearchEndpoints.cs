using Broker.Helpers;
using Broker.Services;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// AI 自主策略研究 API
///
///   GET  /api/v1/research/status               — LLM / worker 連線狀態
///   POST /api/v1/research/start                — 啟動一次研究 run
///                                                  body: { symbol, family, generations, data_limit }
///   GET  /api/v1/research/runs                 — 所有 runs 摘要（最新在前）
///   GET  /api/v1/research/runs/{id}            — 單一 run 的完整 lineage + 所有候選
/// </summary>
public static class ResearchEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var r = group.MapGroup("/research");

        r.MapGet("/status", (StrategyResearchLoopService svc, StrategyGeneratorService gen) =>
        {
            return Results.Ok(ApiResponseHelper.Success(new
            {
                enabled = svc.IsEnabled,
                llm_enabled = gen.IsEnabled,
                supported_families = new[] { "sma_cross", "rsi_oversold" },
            }));
        });

        r.MapPost("/start", async (StrategyResearchLoopService svc, HttpRequest req, CancellationToken ct) =>
        {
            if (!svc.IsEnabled)
                return Results.Ok(ApiResponseHelper.Error("Research loop not available: LLM or workers disconnected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            JsonElement doc;
            try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).RootElement; }
            catch { return Results.Ok(ApiResponseHelper.Error("Invalid JSON body")); }

            var symbol = doc.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()!.ToUpperInvariant() : "AAPL";
            var family = doc.TryGetProperty("family", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()! : "sma_cross";
            var generations = doc.TryGetProperty("generations", out var g) && g.TryGetInt32(out var gi)
                ? Math.Clamp(gi, 1, 10) : 3;
            var dataLimit = doc.TryGetProperty("data_limit", out var dl) && dl.TryGetInt32(out var dli)
                ? Math.Clamp(dli, 100, 5000) : 300;

            var run = await svc.RunAsync(symbol, family, generations, dataLimit, ct);
            return Results.Ok(ApiResponseHelper.Success(run));
        });

        r.MapGet("/runs", (StrategyCandidateRepository repo) =>
        {
            var summaries = repo.GetAll().Select(run => new
            {
                run_id = run.RunId,
                symbol = run.Symbol,
                family = run.Family,
                status = run.Status,
                started_at = run.StartedAt,
                completed_at = run.CompletedAt,
                target_generations = run.TargetGenerations,
                candidate_count = run.Candidates.Count,
                best_sharpe = run.Best?.OutOfSampleSharpe ?? 0m,
                best_return_pct = run.Best?.ReturnPct ?? 0m,
                best_params = run.Best?.Parameters ?? new(),
                error = run.Error,
            });
            return Results.Ok(ApiResponseHelper.Success(summaries));
        });

        r.MapGet("/runs/{runId}", (string runId, StrategyCandidateRepository repo) =>
        {
            var run = repo.Get(runId);
            if (run == null) return Results.Ok(ApiResponseHelper.Error($"Run not found: {runId}"));
            return Results.Ok(ApiResponseHelper.Success(run));
        });
    }
}
