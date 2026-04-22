using System.Text.Json;
using WorkerSdk;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using static StrategyWorker.Engine.BacktestEngine;

namespace StrategyWorker.Handlers;

/// <summary>
/// strategy.signal — 產生交易訊號。
///
/// Routes:
///   evaluate — 用指定策略分析 K 線資料（參數：strategy, bars, config）
///   list     — 列出可用策略
///
/// bars 格式：[{open_time, open, high, low, close, volume}, ...]
/// </summary>
public class StrategySignalHandler : ICapabilityHandler
{
    private readonly Dictionary<string, IStrategy> _strategies;
    public string CapabilityId => "strategy.signal";

    public StrategySignalHandler(Dictionary<string, IStrategy> strategies)
    {
        _strategies = strategies;
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var result = route switch
        {
            "evaluate"     => Evaluate(payload),
            "backtest"     => Backtest(payload),
            "optimize"     => Optimize(payload),
            "walk_forward" => WalkForward(payload),
            "list"         => ListStrategies(),
            _ => (false, (string?)null, $"Unknown route: {route}")
        };
        return Task.FromResult(result);
    }

    private (bool, string?, string?) Evaluate(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;

        // 解析策略名稱
        var strategyName = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "composite" : "composite";

        if (!_strategies.TryGetValue(strategyName, out var strategy))
            return (false, null, $"Unknown strategy: {strategyName}. Available: {string.Join(", ", _strategies.Keys)}");

        // 解析 K 線資料
        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing or invalid 'bars' array");

        var bars = new List<BarData>();
        foreach (var b in barsEl.EnumerateArray())
        {
            bars.Add(new BarData
            {
                OpenTime = b.TryGetProperty("open_time", out var ot) ? DateTime.Parse(ot.GetString()!) : DateTime.MinValue,
                Open     = b.TryGetProperty("open",  out var o)  ? o.GetDecimal()  : 0,
                High     = b.TryGetProperty("high",  out var h)  ? h.GetDecimal()  : 0,
                Low      = b.TryGetProperty("low",   out var l)  ? l.GetDecimal()  : 0,
                Close    = b.TryGetProperty("close", out var c)  ? c.GetDecimal()  : 0,
                Volume   = b.TryGetProperty("volume", out var v) ? v.GetDecimal()  : 0,
            });
        }

        if (bars.Count < 2)
            return (false, null, "Need at least 2 bars");

        // 解析設定
        var config = new StrategyConfig
        {
            Name     = strategyName,
            Symbol   = doc.TryGetProperty("symbol",   out var sym)  ? sym.GetString() ?? ""   : "",
            Exchange = doc.TryGetProperty("exchange",  out var exg)  ? exg.GetString() ?? ""   : "",
            Interval = doc.TryGetProperty("interval",  out var iv)   ? iv.GetString() ?? "1d"  : "1d",
            SmaFast  = doc.TryGetProperty("sma_fast",  out var sf)   ? sf.GetInt32()           : 10,
            SmaSlow  = doc.TryGetProperty("sma_slow",  out var ss)   ? ss.GetInt32()           : 30,
            RsiPeriod     = doc.TryGetProperty("rsi_period",     out var rp) ? rp.GetInt32() : 14,
            RsiOversold   = doc.TryGetProperty("rsi_oversold",   out var ro) ? ro.GetDecimal() : 30,
            RsiOverbought = doc.TryGetProperty("rsi_overbought", out var rb) ? rb.GetDecimal() : 70,
            MacdFast   = doc.TryGetProperty("macd_fast",   out var mf) ? mf.GetInt32() : 12,
            MacdSlow   = doc.TryGetProperty("macd_slow",   out var ms) ? ms.GetInt32() : 26,
            MacdSignal = doc.TryGetProperty("macd_signal", out var mg) ? mg.GetInt32() : 9,
        };

        var signal = strategy.Evaluate(bars, config);

        var json = JsonSerializer.Serialize(new
        {
            signal_id  = signal.SignalId,
            strategy   = signal.Strategy,
            symbol     = signal.Symbol,
            exchange   = signal.Exchange,
            action     = signal.Action,
            confidence = signal.Confidence,
            reason     = signal.Reason,
            interval   = signal.Interval,
            timestamp  = signal.Timestamp,
            indicators = signal.Indicators,
        });
        return (true, json, null);
    }

    private (bool, string?, string?) Backtest(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;
        var strategyName = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "composite" : "composite";

        if (!_strategies.TryGetValue(strategyName, out var strategy))
            return (false, null, $"Unknown strategy: {strategyName}");

        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'bars' array");

        var bars = new List<BarData>();
        foreach (var b in barsEl.EnumerateArray())
        {
            bars.Add(new BarData
            {
                OpenTime = b.TryGetProperty("open_time", out var ot) ? DateTime.Parse(ot.GetString()!) : DateTime.MinValue,
                Open  = b.TryGetProperty("open",  out var o) ? o.GetDecimal() : 0,
                High  = b.TryGetProperty("high",  out var h) ? h.GetDecimal() : 0,
                Low   = b.TryGetProperty("low",   out var l) ? l.GetDecimal() : 0,
                Close = b.TryGetProperty("close", out var c) ? c.GetDecimal() : 0,
                Volume = b.TryGetProperty("volume", out var v) ? v.GetDecimal() : 0,
            });
        }

        var config = new StrategyConfig
        {
            Name     = strategyName,
            Symbol   = doc.TryGetProperty("symbol",   out var sym) ? sym.GetString() ?? "" : "",
            Exchange = doc.TryGetProperty("exchange",  out var exg) ? exg.GetString() ?? "" : "",
            SmaFast  = doc.TryGetProperty("sma_fast",  out var sf)  ? sf.GetInt32()  : 10,
            SmaSlow  = doc.TryGetProperty("sma_slow",  out var ss)  ? ss.GetInt32()  : 30,
            RsiPeriod = doc.TryGetProperty("rsi_period", out var rp) ? rp.GetInt32() : 14,
        };

        var initialCash = doc.TryGetProperty("initial_cash", out var ic) ? ic.GetDecimal() : 100_000m;
        var commission  = doc.TryGetProperty("commission",   out var cm) ? cm.GetDecimal() : 0.001m;

        var result = BacktestEngine.Run(strategy, bars, config, initialCash, commission);

        var json = JsonSerializer.Serialize(new
        {
            strategy = result.Strategy, symbol = result.Symbol,
            initial_cash = result.InitialCash, final_value = result.FinalValue,
            total_return = result.TotalReturn, total_return_pct = result.TotalReturnPct,
            total_trades = result.TotalTrades, win_trades = result.WinTrades, lose_trades = result.LoseTrades,
            win_rate = result.WinRate, max_drawdown = result.MaxDrawdown, max_drawdown_pct = result.MaxDrawdownPct,
            sharpe_ratio = result.SharpeRatio, avg_win = result.AvgWin, avg_loss = result.AvgLoss,
            profit_factor = result.ProfitFactor,
            start_date = result.StartDate, end_date = result.EndDate, total_bars = result.TotalBars,
            trades = result.Trades.Select(t => new
            {
                side = t.Side, entry_date = t.EntryDate, entry_price = t.EntryPrice,
                exit_date = t.ExitDate, exit_price = t.ExitPrice, quantity = t.Quantity,
                pnl = t.Pnl, pnl_pct = t.PnlPct, hold_bars = t.HoldBars,
            }),
            equity_curve = result.EquityCurve.Select(e => new { date = e.Date, value = e.Value }),
        });
        return (true, json, null);
    }

    private (bool, string?, string?) Optimize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (false, null, "Missing payload");
        var doc = JsonDocument.Parse(payload).RootElement;
        var strategy = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "sma_cross" : "sma_cross";

        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'bars' array");

        var bars = new List<BarData>();
        foreach (var b in barsEl.EnumerateArray())
            bars.Add(new BarData
            {
                OpenTime = b.TryGetProperty("open_time", out var ot) ? DateTime.Parse(ot.GetString()!) : DateTime.MinValue,
                Open = b.TryGetProperty("open", out var o) ? o.GetDecimal() : 0,
                High = b.TryGetProperty("high", out var h) ? h.GetDecimal() : 0,
                Low = b.TryGetProperty("low", out var l) ? l.GetDecimal() : 0,
                Close = b.TryGetProperty("close", out var c) ? c.GetDecimal() : 0,
                Volume = b.TryGetProperty("volume", out var v) ? v.GetDecimal() : 0,
            });

        var config = new StrategyConfig
        {
            Symbol = doc.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "",
            Exchange = doc.TryGetProperty("exchange", out var exg) ? exg.GetString() ?? "" : "",
        };
        var cash = doc.TryGetProperty("initial_cash", out var ic) ? ic.GetDecimal() : 100_000m;

        var result = strategy switch
        {
            "sma_cross" => ParameterOptimizer.OptimizeSma(bars, config, cash),
            "rsi_oversold" => ParameterOptimizer.OptimizeRsi(bars, config, cash),
            _ => null
        };

        if (result == null) return (false, null, $"Optimizer not available for: {strategy}. Use sma_cross or rsi_oversold.");

        var json = JsonSerializer.Serialize(new
        {
            result.Strategy, result.Symbol, total_combinations = result.TotalCombinations,
            best_sharpe = result.BestSharpe, best_return = result.BestReturn, best_win_rate = result.BestWinRate,
            best_params = result.BestParams,
            top_results = result.TopResults.Select(r => new
            {
                r.Params, total_return_pct = r.TotalReturnPct, sharpe = r.Sharpe,
                win_rate = r.WinRate, max_drawdown_pct = r.MaxDrawdownPct, trades = r.Trades,
            }),
        });
        return (true, json, null);
    }

    private (bool, string?, string?) WalkForward(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (false, null, "Missing payload");
        var doc = JsonDocument.Parse(payload).RootElement;
        var strategy = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "sma_cross" : "sma_cross";

        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'bars' array");

        var bars = new List<BarData>();
        foreach (var b in barsEl.EnumerateArray())
            bars.Add(new BarData
            {
                OpenTime = b.TryGetProperty("open_time", out var ot) ? DateTime.Parse(ot.GetString()!) : DateTime.MinValue,
                Open  = b.TryGetProperty("open",  out var o) ? o.GetDecimal() : 0,
                High  = b.TryGetProperty("high",  out var h) ? h.GetDecimal() : 0,
                Low   = b.TryGetProperty("low",   out var l) ? l.GetDecimal() : 0,
                Close = b.TryGetProperty("close", out var c) ? c.GetDecimal() : 0,
                Volume = b.TryGetProperty("volume", out var v) ? v.GetDecimal() : 0,
            });

        var config = new StrategyConfig
        {
            Symbol = doc.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "",
            Exchange = doc.TryGetProperty("exchange", out var exg) ? exg.GetString() ?? "" : "",
        };
        var cash = doc.TryGetProperty("initial_cash", out var ic) ? ic.GetDecimal() : 100_000m;
        var trainBars = doc.TryGetProperty("train_bars", out var tb) ? tb.GetInt32() : 200;
        var testBars  = doc.TryGetProperty("test_bars",  out var tt) ? tt.GetInt32() : 50;

        var result = strategy switch
        {
            "sma_cross"    => WalkForwardOptimizer.RunSma(bars, config, trainBars, testBars, cash),
            "rsi_oversold" => WalkForwardOptimizer.RunRsi(bars, config, trainBars, testBars, cash),
            _ => null
        };

        if (result == null)
            return (false, null, $"Walk-forward not available for: {strategy}. Use sma_cross or rsi_oversold.");

        if (result.WindowCount == 0)
            return (false, null, $"Not enough bars: {bars.Count} < {trainBars + testBars} (train + test)");

        var json = JsonSerializer.Serialize(new
        {
            result.Strategy, result.Symbol,
            total_bars = result.TotalBars,
            window_count = result.WindowCount,
            initial_train_bars = result.InitialTrainBars,
            test_bars = result.TestBars,
            avg_in_sample_sharpe = result.AvgInSampleSharpe,
            avg_out_of_sample_sharpe = result.AvgOutOfSampleSharpe,
            degradation_ratio = result.DegradationRatio,
            aggregate_oos_return_pct = result.AggregateOosReturnPct,
            aggregate_oos_win_rate = result.AggregateOosWinRate,
            windows = result.Windows.Select(w => new
            {
                index = w.Index,
                train_range = new { from = w.TrainFrom, to = w.TrainTo, start = w.TrainStartDate },
                test_range  = new { from = w.TestFrom, to = w.TestTo, start = w.TestStartDate, end = w.TestEndDate },
                best_params = w.BestParams,
                in_sample_sharpe = w.InSampleSharpe,
                in_sample_return_pct = w.InSampleReturnPct,
                out_of_sample_sharpe = w.OutOfSampleSharpe,
                out_of_sample_return_pct = w.OutOfSampleReturnPct,
                out_of_sample_win_rate = w.OutOfSampleWinRate,
                out_of_sample_max_drawdown_pct = w.OutOfSampleMaxDrawdownPct,
                out_of_sample_trades = w.OutOfSampleTrades,
            }),
        });
        return (true, json, null);
    }

    private (bool, string?, string?) ListStrategies()
    {
        var json = JsonSerializer.Serialize(new
        {
            strategies = _strategies.Keys.ToList(),
            descriptions = new Dictionary<string, string>
            {
                ["sma_cross"]       = "SMA Golden/Death Cross — 快慢均線交叉",
                ["rsi_oversold"]    = "RSI Oversold/Overbought — 超買超賣",
                ["macd_divergence"] = "MACD Crossover — MACD 與 Signal 交叉",
                ["composite"]      = "Composite — 固定等權投票（SMA + RSI + MACD）",
                ["ensemble"]       = "Ensemble — 動態加權投票，權重 = 成員近期 Sharpe（適應市場變化）",
                ["fibonacci_retracement"] = "Fibonacci Retracement — 在擺動高低點的 0.382-0.618 黃金區偵測順勢回撤進場",
                ["llm"]             = "LLM — AI 模型分析市場資料產生訊號",
                ["multi_timeframe"] = "Multi-Timeframe — 多時間框架交叉確認",
                ["news_sentiment"]  = "News Sentiment — AI 分析財經新聞情緒",
            }
        });
        return (true, json, null);
    }
}
