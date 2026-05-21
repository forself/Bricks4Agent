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
    private readonly IStrategyRegistry _registry;
    public string CapabilityId => "strategy.signal";

    public StrategySignalHandler(IStrategyRegistry registry)
    {
        _registry = registry;
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var result = route switch
        {
            "evaluate"     => Evaluate(payload),
            "backtest"     => Backtest(payload),
            "backtest_batch" => BacktestBatch(payload),                       // 一次跑多策略（bars 只送一次、worker 端跨核心平行）
            "optimize"     => Optimize(payload),
            "optimize_wf"  => OptimizeWf(payload),                            // 通用 walk-forward 參數優化（任何有 ParamSchema 的策略）
            "walk_forward" => WalkForward(payload),                          // 既有 optimizer 用
            "backtest_walk_forward" => BacktestWalkForward(payload),         // #1 新：通用 train/test 滑窗
            "harmonic_aggregate" => HarmonicAggregate(payload),              // 策略級 EV / 勝率彙整
            "scan"         => Scan(payload),                                 // universe 掃描 → Top N 候選
            "position_decision" => PositionDecide(payload),                  // 持倉 ADD/HOLD/TRIM/EXIT
            "signal_card"  => SignalCard(payload),                           // 多維訊號雷達卡(no LLM)
            "list"         => ListStrategies(),
            _ => (false, (string?)null, $"Unknown route: {route}")
        };
        return Task.FromResult(result);
    }

    /// <summary>
    /// scan — universe 掃描:對多個 symbol 各跑 harmonic + price action + SMC 評分,
    /// 回傳依 magnitude 由大到小的 Top N 候選。
    /// payload: { "symbols": { "BTCUSDT": [bars...], "ETHUSDT": [bars...] },
    ///            "min_magnitude"?: 2.0, "top_n"?: 10, "pivot_window"?: 3 }
    /// bars 格式同 evaluate;資料由呼叫端提供(worker 不抓行情)。
    /// </summary>
    private (bool, string?, string?) Scan(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;
        if (!doc.TryGetProperty("symbols", out var symsEl) || symsEl.ValueKind != JsonValueKind.Object)
            return (false, null, "Missing or invalid 'symbols' object (expect { symbol: [bars...] })");

        var minMagnitude = doc.TryGetProperty("min_magnitude", out var mm) ? mm.GetDecimal() : 2.0m;
        var topN         = doc.TryGetProperty("top_n", out var tn) ? tn.GetInt32() : 10;
        var pivotWindow  = doc.TryGetProperty("pivot_window", out var pw) ? pw.GetInt32() : 3;

        var universe = new List<KeyValuePair<string, List<BarData>>>();
        foreach (var sym in symsEl.EnumerateObject())
        {
            if (sym.Value.ValueKind != JsonValueKind.Array) continue;
            universe.Add(new KeyValuePair<string, List<BarData>>(sym.Name, ParseBars(sym.Value)));
        }

        var top = ScannerEngine.ScanUniverse(universe, minMagnitude, topN, pivotWindow);

        var json = JsonSerializer.Serialize(new
        {
            scanned       = universe.Count,
            min_magnitude = minMagnitude,
            top_n         = topN,
            count         = top.Count,
            candidates    = top.Select(r => new
            {
                symbol               = r.Symbol,
                current_price        = r.CurrentPrice,
                bull_score           = r.BullScore,
                bear_score           = r.BearScore,
                net_score            = r.NetScore,
                magnitude            = r.Magnitude,
                direction            = r.Direction,
                bullish_signal_count = r.BullishSignalCount,
                bearish_signal_count = r.BearishSignalCount,
                bullish_signals      = r.BullishSignals,
                bearish_signals      = r.BearishSignals,
            }).ToList(),
        });
        return (true, json, null);
    }

    /// <summary>
    /// position_decision — 對已開倉位給 ADD/HOLD/TRIM/EXIT 建議 + 信心 + 目標價。
    /// payload: { symbol, cost_basis, quantity, current_price, side?, technical_signal?, technical_score?,
    ///            risk_level?, atr?, hold_days?, mtf_bullish?, mtf_bearish?, mtf_total?, news_score?, fundamental_score? }
    /// </summary>
    private (bool, string?, string?) PositionDecide(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var d = JsonDocument.Parse(payload).RootElement;
        decimal Dec(string k, decimal def = 0m) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : def;
        decimal? DecN(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
        int Int(string k, int def = 0) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
        int? IntN(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        string Str(string k, string def) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? def) : def;

        if (Dec("cost_basis") <= 0 || Dec("quantity") <= 0 || Dec("current_price") <= 0)
            return (false, null, "cost_basis / quantity / current_price required and must be > 0");

        PositionDecisionEngine.Result result;
        try
        {
            result = PositionDecisionEngine.Decide(new PositionDecisionEngine.Input
            {
                Symbol           = Str("symbol", ""),
                CostBasis        = Dec("cost_basis"),
                Quantity         = Dec("quantity"),
                CurrentPrice     = Dec("current_price"),
                Side             = Str("side", "long"),
                TechnicalSignal  = Str("technical_signal", "neutral"),
                TechnicalScore   = Dec("technical_score"),
                RiskLevel        = Str("risk_level", "medium"),
                Atr              = DecN("atr"),
                HoldDays         = IntN("hold_days"),
                MtfBullish       = Int("mtf_bullish"),
                MtfBearish       = Int("mtf_bearish"),
                MtfTotal         = Int("mtf_total"),
                NewsScore        = DecN("news_score"),
                FundamentalScore = Int("fundamental_score"),
            });
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }

        var json = JsonSerializer.Serialize(new
        {
            symbol          = result.Symbol,
            decision        = result.Decision.ToString().ToUpperInvariant(),
            base_decision   = result.BaseDecision.ToString().ToUpperInvariant(),
            confidence      = result.Confidence,
            pnl             = result.Pnl,
            pnl_pct         = result.PnlPct,
            signal_strength = result.SignalStrength,
            modifier_total  = result.ModifierTotal,
            targets = new
            {
                stop_loss     = result.StopLoss,
                take_profit_1 = result.TakeProfit1,
                take_profit_2 = result.TakeProfit2,
                add_price     = result.AddPrice,
            },
            reason   = result.Reason,
            evidence = result.Evidence,
        });
        return (true, json, null);
    }

    /// <summary>
    /// signal_card — 單一 symbol 的多維訊號雷達卡(不呼叫 LLM)。
    /// payload: { symbol, bars: [...], mtf_bullish?, mtf_bearish?, mtf_total?, funding_score? }
    /// </summary>
    private (bool, string?, string?) SignalCard(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var d = JsonDocument.Parse(payload).RootElement;
        var symbol = d.TryGetProperty("symbol", out var sy) ? sy.GetString() ?? "" : "";
        if (!d.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing or invalid 'bars' array");

        var bars = ParseBars(barsEl);
        int? IntN(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        var card = SignalFeedEngine.Build(symbol, bars,
            IntN("mtf_bullish"), IntN("mtf_bearish"), IntN("mtf_total"), IntN("funding_score"));
        if (card == null)
            return (false, null, $"Need ≥ {SignalFeedEngine.MinBars} bars (got {bars.Count})");

        var json = JsonSerializer.Serialize(new
        {
            symbol        = card.Symbol,
            direction     = card.Direction,
            confidence    = card.Confidence,
            stars         = card.Stars,
            tag           = card.Tag,
            current_price = card.CurrentPrice,
            change_pct    = card.ChangePct,
            trigger_price = card.TriggerPrice,
            avg_winrate   = card.AvgWinrate,
            radar         = card.Radar,
        });
        return (true, json, null);
    }

    private (bool, string?, string?) Evaluate(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;

        // 解析策略名稱
        var strategyName = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "composite" : "composite";

        var strategy = _registry.Get(strategyName);
        if (strategy == null)
            return (false, null, $"Unknown strategy: {strategyName}. Available: {string.Join(", ", _registry.Names())}");

        // 解析 K 線資料
        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing or invalid 'bars' array");

        var bars = ParseBars(barsEl);

        if (bars.Count < 2)
            return (false, null, "Need at least 2 bars");

        // 解析 HTF bars（Batch C+++、影片大週期優先）
        List<BarData>? htfBars = null;
        if (doc.TryGetProperty("htf_bars", out var htfEl) && htfEl.ValueKind == JsonValueKind.Array)
        {
            var parsed = ParseBars(htfEl);
            if (parsed.Count >= 2) htfBars = parsed;
        }

        // 解析設定
        var config = new StrategyConfig
        {
            Name     = strategyName,
            Symbol   = doc.TryGetProperty("symbol",   out var sym)  ? sym.GetString() ?? ""   : "",
            Exchange = doc.TryGetProperty("exchange",  out var exg)  ? exg.GetString() ?? ""   : "",
            Interval = doc.TryGetProperty("interval",  out var iv)   ? iv.GetString() ?? "1d"  : "1d",
            HtfBars  = htfBars,
            HtfInterval = doc.TryGetProperty("htf_interval", out var hi) ? hi.GetString() : null,
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

        // Regime side-channel：跟 signal 同一份 bars 算一次 regime、broker AutoTrader 用它做 gate。
        // 算一次 ~微秒等級、不增加感知延遲；放在 response 比讓 broker 自己再 call detect_regime 省一個 round trip。
        var regime = StrategyWorker.Engine.Indicators.RegimeDetector.Detect(bars);

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
            regime = new
            {
                type         = regime.Type.ToString().ToLowerInvariant(),  // unclear/trendingup/trendingdown/rangebound/squeeze/highvol
                sma50_slope  = regime.Sma50Slope,
                atr_pct      = regime.AtrPct,
                bb_width     = regime.BbWidth,
                above_sma50  = regime.AboveSma50,
                description  = regime.Description,
            },
        });
        return (true, json, null);
    }

    // 共用 K 線陣列 → List<BarData> 解析（避免每個 route 重複貼）
    private static List<BarData> ParseBars(System.Text.Json.JsonElement barsEl)
    {
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
        return bars;
    }

    private (bool, string?, string?) Backtest(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;
        var strategyName = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "composite" : "composite";

        var strategy = _registry.Get(strategyName);
        if (strategy == null)
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

        // Batch HTF backtest：可選 htf_bars[] + htf_interval（HarmonicStrategy 等多時間框架策略用）
        List<BarData>? htfBars = null;
        if (doc.TryGetProperty("htf_bars", out var htfElB) && htfElB.ValueKind == JsonValueKind.Array)
        {
            var parsed = ParseBars(htfElB);
            if (parsed.Count >= 2) htfBars = parsed;
        }
        if (doc.TryGetProperty("htf_interval", out var hiB)) config.HtfInterval = hiB.GetString();

        var result = BacktestEngine.Run(strategy, bars, config, initialCash, commission, htfBars);

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

    /// <summary>
    /// backtest_batch — 對「同一份 bars」一次跑多個策略（每個 = backtest + walk-forward），
    /// worker 端用 Parallel.ForEach 跨核心平行。解決 broker 把同一份 1000-bar payload 對每個策略
    /// 重複序列化派 N 次的吞吐瓶頸：bars 只送一次、round-trip 從 N 降到 1、6 核吃滿。
    /// payload: { bars, symbol, exchange, interval, strategies:[...], initial_cash?, commission?,
    ///            train_bars?, test_bars?, stride?, walk_forward?(bool 預設 true) }
    /// </summary>
    private (bool, string?, string?) BacktestBatch(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (false, null, "Missing payload");
        var doc = JsonDocument.Parse(payload).RootElement;

        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'bars' array");
        var bars = ParseBars(barsEl);
        if (bars.Count < 50) return (false, null, $"Need >= 50 bars (got {bars.Count})");

        if (!doc.TryGetProperty("strategies", out var stratsEl) || stratsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'strategies' array");
        var names = stratsEl.EnumerateArray()
            .Select(s => s.GetString() ?? "").Where(s => s.Length > 0).Distinct().ToList();

        var cfg = new StrategyConfig
        {
            Symbol   = doc.TryGetProperty("symbol",   out var sy) ? sy.GetString() ?? "" : "",
            Exchange = doc.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "",
            Interval = doc.TryGetProperty("interval", out var iv) ? iv.GetString() ?? "1d" : "1d",
        };
        var cash       = doc.TryGetProperty("initial_cash", out var ic) ? ic.GetDecimal() : 1000m;
        var commission = doc.TryGetProperty("commission",   out var cm) ? cm.GetDecimal() : 0.001m;
        var trainBars  = doc.TryGetProperty("train_bars",   out var tb) ? tb.GetInt32() : 365;
        var testBars   = doc.TryGetProperty("test_bars",    out var tt) ? tt.GetInt32() : 90;
        var stride     = doc.TryGetProperty("stride",       out var st) ? st.GetInt32() : 90;
        var withWf     = !doc.TryGetProperty("walk_forward", out var wfv) || wfv.ValueKind != JsonValueKind.False;

        var results = new System.Collections.Concurrent.ConcurrentBag<object>();
        Parallel.ForEach(names,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            name =>
            {
                var strat = _registry.Get(name);
                if (strat == null) { results.Add(new { strategy = name, error = "unknown strategy" }); return; }
                try
                {
                    var bt = Run(strat, bars, cfg, cash, commission);
                    int folds = 0; decimal oosRet = 0m, oosSharpe = 0m, oosWin = 0m, isOosGap = 0m;
                    if (withWf)
                    {
                        try
                        {
                            var w = RunWalkForward(strat, bars, cfg, trainBars, testBars, stride, cash, commission);
                            folds = w.TotalFolds; oosRet = w.AvgTestReturnPct; oosSharpe = w.AvgTestSharpe;
                            oosWin = w.AvgTestWinRate; isOosGap = w.IsOosReturnGap;
                        }
                        catch { /* IS-only：walk-forward 失敗不擋主回測 */ }
                    }
                    results.Add(new
                    {
                        strategy         = name,
                        total_return_pct = bt.TotalReturnPct,
                        sharpe_ratio     = bt.SharpeRatio,
                        win_rate         = bt.WinRate,
                        max_drawdown_pct = bt.MaxDrawdownPct,
                        total_trades     = bt.TotalTrades,
                        wf_folds         = folds,
                        oos_return_pct   = oosRet,
                        oos_sharpe       = oosSharpe,
                        oos_win_rate     = oosWin,
                        is_oos_gap       = isOosGap,
                    });
                }
                catch (Exception ex)
                {
                    var msg = ex.Message?.Length > 200 ? ex.Message[..200] : ex.Message;
                    results.Add(new { strategy = name, error = msg });
                }
            });

        var json = JsonSerializer.Serialize(new
        {
            symbol = cfg.Symbol, interval = cfg.Interval, bars_count = bars.Count,
            count = results.Count, results = results.ToList(),
        });
        return (true, json, null);
    }

    /// <summary>
    /// optimize_wf — 通用 walk-forward 參數優化：對任何「有 ParamSchema」的策略掃參數空間,
    /// 回「優化後 OOS」vs「預設參數 OOS」對照,判斷調參能否救 OOS。
    /// payload: { strategy, bars, symbol, exchange, interval, train_bars?, test_bars?, initial_cash? }
    /// </summary>
    private (bool, string?, string?) OptimizeWf(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (false, null, "Missing payload");
        var doc = JsonDocument.Parse(payload).RootElement;

        var strategyName = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "" : "";
        var strat = _registry.Get(strategyName);
        if (strat == null) return (false, null, $"Unknown strategy: {strategyName}");

        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'bars' array");
        var bars = ParseBars(barsEl);

        var cfg = new StrategyConfig
        {
            Name = strategyName,
            Symbol   = doc.TryGetProperty("symbol",   out var sy) ? sy.GetString() ?? "" : "",
            Exchange = doc.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "",
            Interval = doc.TryGetProperty("interval", out var iv) ? iv.GetString() ?? "1d" : "1d",
        };
        var trainBars = doc.TryGetProperty("train_bars", out var tb) ? tb.GetInt32() : 365;
        var testBars  = doc.TryGetProperty("test_bars",  out var tt) ? tt.GetInt32() : 90;
        var cash      = doc.TryGetProperty("initial_cash", out var ic) ? ic.GetDecimal() : 1000m;

        var r = GenericWalkForwardOptimizer.Optimize(strat, bars, cfg, trainBars, testBars, cash);

        var json = JsonSerializer.Serialize(new
        {
            strategy = r.Strategy, symbol = r.Symbol,
            grid_size = r.GridSize, window_count = r.WindowCount,
            train_bars = r.TrainBars, test_bars = r.TestBars,
            opt_oos_return_pct = r.OptOosReturnPct, opt_oos_sharpe = r.OptOosSharpe, opt_oos_win_rate = r.OptOosWinRate,
            def_oos_return_pct = r.DefOosReturnPct, def_oos_sharpe = r.DefOosSharpe,
            most_common_best_params = r.MostCommonBestParams,
            error = r.Error,
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
            "sma_cross"        => ParameterOptimizer.OptimizeSma(bars, config, cash),
            "rsi_oversold"     => ParameterOptimizer.OptimizeRsi(bars, config, cash),
            "macd_divergence"  => ParameterOptimizer.OptimizeMacd(bars, config, cash),
            _ => null
        };

        if (result == null) return (false, null, $"Optimizer not available for: {strategy}. Use sma_cross / rsi_oversold / macd_divergence.");

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

    // ── #1 通用 walk-forward backtest ──────────────────────────────
    //
    // 比上面 `walk_forward`（只支援 sma_cross/rsi 的參數優化）泛用：對任何已註冊策略
    // 切 [train, test, train, test, ...] 滑動視窗、回傳每個 fold 的 in-sample / OOS
    // 績效，並聚合成 OOS 平均報酬 / Sharpe / 勝率 + IS-OOS gap（過擬合指標）。
    private (bool, string?, string?) BacktestWalkForward(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (false, null, "Missing payload");
        var doc = JsonDocument.Parse(payload).RootElement;

        var strategyName = doc.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "composite" : "composite";
        var strategy = _registry.Get(strategyName);
        if (strategy == null)
            return (false, null, $"Unknown strategy: {strategyName}");

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
            Symbol   = doc.TryGetProperty("symbol",   out var sym) ? sym.GetString() ?? "" : "",
            Exchange = doc.TryGetProperty("exchange", out var exg) ? exg.GetString() ?? "" : "",
            Interval = doc.TryGetProperty("interval", out var iv)  ? iv.GetString() ?? "1d" : "1d",
        };

        var cash       = doc.TryGetProperty("initial_cash", out var ic) ? ic.GetDecimal() : 100_000m;
        var commission = doc.TryGetProperty("commission", out var cm) ? cm.GetDecimal() : 0.001m;
        var trainBars  = doc.TryGetProperty("train_bars", out var tb) ? tb.GetInt32() : 180;
        var testBars   = doc.TryGetProperty("test_bars",  out var tt) ? tt.GetInt32() : 60;
        var stride     = doc.TryGetProperty("stride",     out var st) ? st.GetInt32() : 30;

        var r = BacktestEngine.RunWalkForward(strategy, bars, config, trainBars, testBars, stride, cash, commission);
        if (r.Folds.Count == 0)
            return (false, null,
                $"Not enough bars or invalid params: bars={bars.Count} train={trainBars} test={testBars} stride={stride}");

        var json = JsonSerializer.Serialize(new
        {
            strategy = r.Strategy, symbol = r.Symbol,
            train_bars = r.TrainBars, test_bars = r.TestBars, stride = r.Stride,
            total_folds = r.TotalFolds, positive_test_folds = r.PositiveTestFolds,
            avg_test_return_pct = r.AvgTestReturnPct,
            median_test_return_pct = r.MedianTestReturnPct,
            avg_test_sharpe = r.AvgTestSharpe,
            avg_test_win_rate = r.AvgTestWinRate,
            worst_test_dd_pct = r.WorstTestDdPct,
            is_oos_return_gap = r.IsOosReturnGap,
            is_oos_sharpe_gap = r.IsOosSharpeGap,
            folds = r.Folds.Select(f => new
            {
                fold_index = f.FoldIndex,
                train_start = f.TrainStart, train_end = f.TrainEnd,
                test_start  = f.TestStart,  test_end  = f.TestEnd,
                train_return_pct  = f.Train?.TotalReturnPct ?? 0,
                train_sharpe     = f.Train?.SharpeRatio ?? 0,
                train_trades     = f.Train?.TotalTrades ?? 0,
                test_return_pct  = f.Test?.TotalReturnPct ?? 0,
                test_sharpe     = f.Test?.SharpeRatio ?? 0,
                test_win_rate   = f.Test?.WinRate ?? 0,
                test_max_drawdown_pct = f.Test?.MaxDrawdownPct ?? 0,
                test_trades     = f.Test?.TotalTrades ?? 0,
                test_equity_curve = f.Test?.EquityCurve.Select(e => new { date = e.Date, value = e.Value }),
            }),
        });
        return (true, json, null);
    }

    // 跨 pattern 策略級 EV 統計（dashboard / API 看 harmonic_pattern 歷史表現）
    private (bool, string?, string?) HarmonicAggregate(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (false, null, "Missing payload");
        var doc = System.Text.Json.JsonDocument.Parse(payload).RootElement;
        if (!doc.TryGetProperty("bars", out var barsEl) || barsEl.ValueKind != System.Text.Json.JsonValueKind.Array)
            return (false, null, "Missing 'bars' array");

        var bars = ParseBars(barsEl);
        if (bars.Count < 30) return (false, null, $"Need ≥ 30 bars for harmonic detection (got {bars.Count})");

        var pivotWindow = doc.TryGetProperty("pivot_window", out var pw) ? pw.GetInt32() : 3;
        var stats = StrategyWorker.Engine.Indicators.HarmonicPatterns.ComputeAggregateStats(bars, pivotWindow);

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            total_detections    = stats.TotalDetections,
            closed_detections   = stats.ClosedDetections,
            tp1_hit_count       = stats.Tp1HitCount,
            tp2_hit_count       = stats.Tp2HitCount,
            sl_hit_count        = stats.SlHitCount,
            invalidated_count   = stats.InvalidatedCount,
            open_count          = stats.OpenCount,
            tp1_hit_pct         = stats.Tp1HitPct,
            tp2_hit_pct         = stats.Tp2HitPct,
            sl_hit_pct          = stats.SlHitPct,
            invalidated_pct     = stats.InvalidatedPct,
            avg_gain_on_tp1     = stats.AvgGainOnTp1,
            avg_gain_on_tp2     = stats.AvgGainOnTp2,
            avg_loss_on_sl      = stats.AvgLossOnSl,
            avg_loss_on_invalid = stats.AvgLossOnInvalid,
            expected_return_pct_tp1_only = stats.ExpectedReturnPctTp1Only,
            expected_return_pct_tp2_only = stats.ExpectedReturnPctTp2Only,
            avg_risk_reward     = stats.AvgRiskReward,
        });
        return (true, json, null);
    }

    private (bool, string?, string?) ListStrategies()
    {
        // 全部從 registry 拿——每個 IStrategy 自己 expose Description / Category /
        // MinBars / MinCapitalUsdt / ParamSchema。broker 端讀回來就能直接餵給
        // dashboard tooltip / Lab MinCapital 檢查 / grid search 維度，不用再寫死表。
        var all = _registry.All();
        var json = JsonSerializer.Serialize(new
        {
            strategies = all.Select(s => s.Name).ToList(),
            // 保留舊欄位讓現有 broker 端 fallback 解析 OK
            descriptions = all.ToDictionary(s => s.Name, s => s.Description),
            metadata = all.Select(s => new
            {
                name             = s.Name,
                description      = s.Description,
                category         = s.Category.ToString(),
                min_bars         = s.MinBars,
                min_capital_usdt = s.MinCapitalUsdt,
                param_schema     = s.ParamSchema.ToDictionary(
                    kv => kv.Key,
                    kv => new
                    {
                        type        = kv.Value.Type,
                        @default    = kv.Value.Default,
                        min         = kv.Value.Min,
                        max         = kv.Value.Max,
                        step        = kv.Value.Step,
                        description = kv.Value.Description,
                        choices     = kv.Value.Choices,
                    }),
            }),
        });
        return (true, json, null);
    }
}
