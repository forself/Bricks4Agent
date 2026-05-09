using Broker.Helpers;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Endpoints;

/// <summary>
/// Strategy Lab API — 自動回測結果查詢 + 推薦過濾。
///
///   GET  /api/v1/lab/recommendations?capital=100&timeframe=1d
///        每個 (symbol, timeframe) 取 recommended=true 的 row、按申報資金過濾出策略適配的、
///        附「最低資金」「適配性」標籤回傳。
///
///   GET  /api/v1/lab/runs?limit=10
///        最近的批次回測 run、看「上次跑何時、結果幾筆」。
///
///   GET  /api/v1/lab/run/{runId}
///        某 run 的所有 results（debug 用）。
///
///   POST /api/v1/lab/run-now
///        立刻觸發一次手動 run（不必等到下次 schedule、結果同樣存 DB）。
///
/// Capital fit metadata：策略對「最低有效資金」的判定。低於這個值跑策略沒意義。
/// 例：grid 需多級才有效（≥ $500）、DCA 需多次入金才有意義（≥ $300）、trend-following $50+ 即可。
/// </summary>
public static class LabEndpoints
{
    /// <summary>
    /// 策略最低有效資金（USDT）。低於這個值該策略列入 recommendation 時打 "too-small" tag、不過濾掉。
    /// 數值是經驗值：越大資金越能讓策略發揮（更多 grid 級數、DCA 平均成本攤更平）。
    /// </summary>
    private static readonly Dictionary<string, decimal> MinCapital = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sma_cross"]            = 50m,    // trend-following，小資金可跑、單筆風險小
        ["rsi_oversold"]         = 50m,    // mean-reversion，類似
        ["macd_divergence"]      = 80m,    // 訊號頻率較低、需資金等待
        ["bollinger_bands"]      = 100m,   // 雙邊出入場、需多 trade 攤平費用
        ["composite"]            = 100m,   // 多策略投票、需符合多條件、出場略密、佣金敏感
        ["multi_timeframe"]      = 150m,   // 多時框確認、入場挑剔、需大資金 ride trend
        ["fibonacci_retracement"]= 200m,   // 黃金區進場、止損偏遠、單筆風險較大
        ["bollinger_bands"]      = 100m,
        ["harmonic_pattern"]     = 300m,   // pattern 檢出稀有、出單頻率低、需大本金週轉
        ["vegas_tunnel"]         = 200m,   // 多層通道、需明顯趨勢
        ["ensemble"]             = 200m,   // 動態加權、複雜邏輯、適合中規模以上
        ["auto_select"]          = 100m,   // 行情切換策略、彈性高
        ["llm"]                  = 500m,   // LLM API cost、頻次低、適合中大規模
        ["news_sentiment"]       = 500m,   // news API + LLM、cost ratio 對小本金不利
    };

    public static void Map(RouteGroupBuilder group)
    {
        var lab = group.MapGroup("/lab");

        lab.MapGet("/recommendations", (BrokerDb db, HttpRequest req) =>
        {
            var capital = req.Query.TryGetValue("capital", out var c) && decimal.TryParse(c, out var cv) ? cv : 0m;
            var tf = req.Query.TryGetValue("timeframe", out var t) ? t.ToString() : null;

            // 取最近一個 finished run
            var latestRun = db.Query<BacktestRunEntry>(
                "SELECT * FROM backtest_runs WHERE finished_at IS NOT NULL ORDER BY started_at DESC LIMIT 1")
                .FirstOrDefault();
            if (latestRun == null)
                return Results.Ok(ApiResponseHelper.Success(new { run = (object?)null, recommendations = Array.Empty<object>() }));

            // recommended=true 那些
            var recsSql = "SELECT * FROM backtest_results WHERE run_id = @rid AND recommended = 1";
            if (!string.IsNullOrEmpty(tf)) recsSql += $" AND timeframe = '{tf.Replace("'", "")}'";
            recsSql += " ORDER BY score DESC";

            var rows = db.Query<BacktestResultEntry>(recsSql, new { rid = latestRun.RunId });

            var enriched = rows.Select(r =>
            {
                var minCap = MinCapital.TryGetValue(r.Strategy, out var m) ? m : 100m;
                var tags = new List<string>();
                if (capital > 0m && capital < minCap) tags.Add("too-small-capital");
                if (r.Trades < 5) tags.Add("low-sample");
                if (r.Sharpe > 1.5m) tags.Add("strong-edge");
                if (r.MaxDdPct > 30m) tags.Add("deep-drawdown");
                return new
                {
                    symbol = r.Symbol,
                    timeframe = r.Timeframe,
                    strategy = r.Strategy,
                    score = r.Score,
                    sharpe = r.Sharpe,
                    total_return_pct = r.TotalReturnPct,
                    win_rate = r.WinRate,
                    max_dd_pct = r.MaxDdPct,
                    trades = r.Trades,
                    min_capital_usdt = minCap,
                    capital_fit = capital <= 0m ? "unknown" : (capital >= minCap ? "ok" : "underfunded"),
                    tags,
                };
            }).ToList();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                run = new
                {
                    run_id = latestRun.RunId,
                    started_at = latestRun.StartedAt,
                    finished_at = latestRun.FinishedAt,
                    duration_ms = latestRun.DurationMs,
                    symbols_count = latestRun.SymbolsCount,
                    results_count = latestRun.ResultsCount,
                    error_count = latestRun.ErrorCount,
                },
                capital_query = capital,
                count = enriched.Count,
                recommendations = enriched,
            }));
        });

        lab.MapGet("/runs", (BrokerDb db, HttpRequest req) =>
        {
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? Math.Clamp(n, 1, 100) : 10;
            var rows = db.Query<BacktestRunEntry>(
                $"SELECT * FROM backtest_runs ORDER BY started_at DESC LIMIT {limit}");
            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = rows.Count,
                runs = rows.Select(r => new
                {
                    run_id = r.RunId,
                    started_at = r.StartedAt,
                    finished_at = r.FinishedAt,
                    run_type = r.RunType,
                    duration_ms = r.DurationMs,
                    symbols_count = r.SymbolsCount,
                    results_count = r.ResultsCount,
                    error_count = r.ErrorCount,
                }),
            }));
        });

        lab.MapGet("/run/{runId}", (BrokerDb db, string runId) =>
        {
            var run = db.Get<BacktestRunEntry>(runId);
            if (run == null) return Results.Ok(ApiResponseHelper.Error("run not found"));
            var results = db.Query<BacktestResultEntry>(
                "SELECT * FROM backtest_results WHERE run_id = @rid ORDER BY score DESC",
                new { rid = runId });
            return Results.Ok(ApiResponseHelper.Success(new
            {
                run, count = results.Count,
                results = results.Select(r => new
                {
                    r.Symbol, r.Timeframe, r.Strategy, r.Score, r.Sharpe,
                    r.TotalReturnPct, r.WinRate, r.MaxDdPct, r.Trades,
                    r.Recommended, r.Error,
                }),
            }));
        });

        lab.MapPost("/run-now", async (ScheduledBacktestService svc, CancellationToken ct) =>
        {
            try
            {
                var runId = await svc.RunOnceAsync("manual", ct);
                return Results.Ok(ApiResponseHelper.Success(new { run_id = runId, status = "completed" }));
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error(ex.Message)); }
        });
    }
}
