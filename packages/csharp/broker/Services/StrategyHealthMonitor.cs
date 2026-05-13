using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 「self-healing autonomous agent」— 偵測表現差的策略 watch 自動 pause、推通知。
///
/// 動機：AutoTrader 一旦 enable、每個 watch 永遠跑、直到 user 手動 pause。但策略表現差時
/// （連虧 / 低 win_rate）繼續跑只會繼續賠 anchor。 thesis Ch 6.2.1 講「最小化模型權限」、
/// 這條延伸到「策略也要被 governance」— 系統自己抓爛策略下架、不依賴人工監控。
///
/// 觸發條件（任一即 retire）：
///   - 連續虧損 N 筆（預設 5）
///   - 已有 ≥10 筆紀錄且 win_rate &lt; 30%
///
/// 動作：
///   - AutoTrader.PauseWatch(symbol, exchange)
///   - 推 Discord + LINE「策略退役」通知
///   - 紀錄到 log（不刪 DB row、user 可手動 resume）
///
/// Cadence：每 30 min sweep 一次（env: AUTOTRADER_HEALTH_CHECK_MIN）。0 = 關閉。
///
/// 對標 starter2：他們沒這個機制；對學術 narrative 是 differentiator。
/// </summary>
public class StrategyHealthMonitor : BackgroundService
{
    private readonly AutoTraderService _autoTrader;
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly DiscordNotificationService? _discord;
    private readonly LineNotificationService? _line;
    private readonly ILogger<StrategyHealthMonitor> _logger;

    private readonly int _checkIntervalMin;
    private readonly int _maxLossStreak;
    private readonly decimal _minWinRatePct;
    private readonly int _minSampleForWinRate;
    private readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(2);

    public StrategyHealthMonitor(
        AutoTraderService autoTrader,
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        ILogger<StrategyHealthMonitor> logger,
        DiscordNotificationService? discord = null,
        LineNotificationService? line = null)
    {
        _autoTrader = autoTrader;
        _dispatcher = dispatcher;
        _registry = registry;
        _discord = discord;
        _line = line;
        _logger = logger;

        _checkIntervalMin = ParseIntEnv("AUTOTRADER_HEALTH_CHECK_MIN", defaultValue: 30, min: 0, max: 1440);
        _maxLossStreak = ParseIntEnv("AUTOTRADER_HEALTH_MAX_LOSS_STREAK", defaultValue: 5, min: 2, max: 50);
        _minWinRatePct = ParseDecimalEnv("AUTOTRADER_HEALTH_MIN_WIN_RATE_PCT", defaultValue: 30m, min: 0m, max: 100m);
        _minSampleForWinRate = ParseIntEnv("AUTOTRADER_HEALTH_MIN_SAMPLES", defaultValue: 10, min: 5, max: 100);

        if (_checkIntervalMin > 0)
            _logger.LogInformation(
                "StrategyHealthMonitor: interval={Min}min, max_loss_streak={S}, min_win_rate={W}% over ≥{N} trades",
                _checkIntervalMin, _maxLossStreak, _minWinRatePct, _minSampleForWinRate);
        else
            _logger.LogInformation("StrategyHealthMonitor: disabled (AUTOTRADER_HEALTH_CHECK_MIN=0)");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_checkIntervalMin <= 0) return;
        try { await Task.Delay(_startupDelay, ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await CheckOnceAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "StrategyHealthMonitor: sweep failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(_checkIntervalMin), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.account")) return;

        var activeWatches = _autoTrader.WatchList.Values.Where(w => w.Active).ToList();
        if (activeWatches.Count == 0) return;

        foreach (var w in activeWatches)
        {
            if (ct.IsCancellationRequested) break;
            try { await CheckWatchAsync(w, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "Health check failed for {Sym}", w.Symbol); }
        }
    }

    private async Task CheckWatchAsync(WatchItem w, CancellationToken ct)
    {
        // 撈該 (exchange, symbol) 過去 30 天 trades、filter strategy
        var since = DateTime.UtcNow.AddDays(-30);
        var payload = JsonSerializer.Serialize(new
        {
            exchange = w.Exchange,
            symbol = w.Symbol,
            limit = 200,
            since = since.ToString("o"),
        });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "trading.account",
            Route = "get_trade_history",
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "strategy-health",
            SessionId = "strategy-health",
        };
        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success) return;

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        if (!doc.TryGetProperty("trades", out var trades) || trades.ValueKind != JsonValueKind.Array) return;

        // 只看「同 strategy 且 realized_pnl != null」、按 executed_at DESC 排
        var pnls = new List<(decimal pnl, DateTime at)>();
        foreach (var t in trades.EnumerateArray())
        {
            var strat = t.TryGetProperty("strategy", out var s) ? s.GetString() : null;
            if (string.IsNullOrEmpty(strat) || strat != w.Strategy) continue;
            if (!t.TryGetProperty("realized_pnl", out var p) || p.ValueKind != JsonValueKind.Number) continue;
            var at = t.TryGetProperty("executed_at", out var ea) && DateTime.TryParse(ea.GetString(), out var d) ? d : DateTime.MinValue;
            pnls.Add((p.GetDecimal(), at));
        }
        if (pnls.Count == 0) return;

        // 按時間 DESC：最新的在前
        pnls.Sort((a, b) => b.at.CompareTo(a.at));

        // 連續虧損計（從最新一筆往回）
        int lossStreak = 0;
        foreach (var (pnl, _) in pnls)
        {
            if (pnl < 0m) lossStreak++;
            else break;
        }

        // win_rate
        int wins = pnls.Count(x => x.pnl > 0m);
        decimal winRatePct = pnls.Count > 0 ? 100m * wins / pnls.Count : 0m;

        // 觸發條件
        string? retireReason = null;
        if (lossStreak >= _maxLossStreak)
            retireReason = $"連虧 {lossStreak} 筆 ≥ {_maxLossStreak}";
        else if (pnls.Count >= _minSampleForWinRate && winRatePct < _minWinRatePct)
            retireReason = $"win_rate {winRatePct:F1}% < {_minWinRatePct}% (over {pnls.Count} trades)";

        if (retireReason == null) return;

        // ── Retire！──
        _autoTrader.PauseWatch(w.Symbol, w.Exchange);
        _logger.LogWarning("StrategyHealth RETIRED {Sym}/{Strat}: {Reason}", w.Symbol, w.Strategy, retireReason);

        var title = $"🪦 策略自動退役 · {w.Symbol} ({w.Strategy})";
        var body = $"原因：{retireReason}\n" +
                   $"近期 {pnls.Count} 筆紀錄：勝 {wins} / 敗 {pnls.Count - wins}\n" +
                   $"AutoTrader 已 pause 此 watch（沒刪除、可手動 resume）。";
        try { if (_discord != null) await _discord.SendAdHocAsync(title, body, color: 0x6c757d, ct); } catch { }
        try { if (_line != null) await _line.SendAdHocAsync(title, body, level: "warning", ct); } catch { }
    }

    private static int ParseIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
    private static decimal ParseDecimalEnv(string name, decimal defaultValue, decimal min, decimal max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (decimal.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
}
