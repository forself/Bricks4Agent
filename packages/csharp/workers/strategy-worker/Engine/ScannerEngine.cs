using System;
using System.Collections.Generic;
using System.Linq;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Universe 掃描引擎 —— 對一籃子標的各跑 harmonic + price action + SMC,
/// 依「多/空訊號強度」算出 net_score / magnitude,回傳依 magnitude 排序的 Top N 候選。
///
/// 用途:進入策略/回測階段時,從大量標的裡先撈出「即將大動」的候選,再對它們建交易規則
/// 或丟回測,不必只盯固定 symbol。
///
/// 評分模型(沿用 ai-quant-starter2/app/services/scanner_engine.py 的設計、C# 重寫、接本專案既有指標):
///   - harmonic(Status=Open) : weight 3 × Confidence
///   - price action          : weight 2 × Confidence
///   - SMC(Signal=buy/sell)  : weight 2 × Confidence
///   net = bull − bear;magnitude = |net|;net &gt; 1 偏多、&lt; −1 偏空、否則中性。
///
/// 設計成純函式:吃「symbol → K 線」,本身不抓資料(universe→K 線由呼叫端餵),
/// 方便單元測試與跨資料源(quote-worker / yfinance / ccxt)重用。
/// </summary>
public static class ScannerEngine
{
    /// <summary>單一標的的掃描評分結果。</summary>
    public sealed class ScanResult
    {
        public string Symbol { get; init; } = "";
        public decimal CurrentPrice { get; init; }
        public decimal BullScore { get; init; }
        public decimal BearScore { get; init; }
        public decimal NetScore { get; init; }
        public decimal Magnitude { get; init; }
        public string Direction { get; init; } = "neutral"; // bullish / bearish / neutral
        public int BullishSignalCount { get; init; }
        public int BearishSignalCount { get; init; }
        public List<string> BullishSignals { get; init; } = new();
        public List<string> BearishSignals { get; init; } = new();
    }

    /// <summary>少於這個 bar 數的標的直接跳過(指標算不出有效訊號)。</summary>
    public const int MinBars = 30;

    // 各訊號類別權重(對齊 scanner_engine.py)
    private const decimal HarmonicWeight = 3.0m;
    private const decimal PriceActionWeight = 2.0m;
    private const decimal SmcWeight = 2.0m;

    /// <summary>對單一 symbol 跑三類訊號評分;bars 不足回 null。</summary>
    public static ScanResult? ScanSymbol(
        string symbol,
        List<BarData> bars,
        int pivotWindow = 3,
        int priceActionMaxAgeBars = 5,
        int harmonicMaxAgeBars = 30)
    {
        if (bars == null || bars.Count < MinBars) return null;

        decimal bull = 0m, bear = 0m;
        var bullSignals = new List<string>();
        var bearSignals = new List<string>();

        // 1) Harmonic —— 只算仍有效(Open)的形態,weight 3 × fit(Confidence)
        foreach (var h in HarmonicPatterns.DetectAll(bars, pivotWindow, maxAgeBars: harmonicMaxAgeBars))
        {
            if (h.Status != HarmonicPatterns.PatternStatus.Open) continue;
            var w = HarmonicWeight * h.Confidence;
            if (h.Direction == "bullish") { bull += w; bullSignals.Add($"{h.PatternName}({h.Confidence:0.00})"); }
            else if (h.Direction == "bearish") { bear += w; bearSignals.Add($"{h.PatternName}({h.Confidence:0.00})"); }
        }

        // 2) Price action —— weight 2 × Confidence
        foreach (var p in PriceActionPatterns.DetectAll(bars, priceActionMaxAgeBars))
        {
            var w = PriceActionWeight * p.Confidence;
            if (p.Direction == PriceActionPatterns.Direction.Bullish) { bull += w; bullSignals.Add(p.Type); }
            else if (p.Direction == PriceActionPatterns.Direction.Bearish) { bear += w; bearSignals.Add(p.Type); }
        }

        // 3) SMC —— 取當前結構訊號(buy/sell),weight 2 × Confidence
        var smc = Smc.Detect(bars, pivotWindow);
        if (smc.Signal == "buy") { bull += SmcWeight * smc.Confidence; bullSignals.Add(smc.SignalType); }
        else if (smc.Signal == "sell") { bear += SmcWeight * smc.Confidence; bearSignals.Add(smc.SignalType); }

        var net = bull - bear;
        var magnitude = Math.Abs(net);
        var direction = net > 1.0m ? "bullish" : net < -1.0m ? "bearish" : "neutral";

        return new ScanResult
        {
            Symbol = symbol,
            CurrentPrice = Math.Round(bars[^1].Close, 4),
            BullScore = Math.Round(bull, 2),
            BearScore = Math.Round(bear, 2),
            NetScore = Math.Round(net, 2),
            Magnitude = Math.Round(magnitude, 2),
            Direction = direction,
            BullishSignalCount = bullSignals.Count,
            BearishSignalCount = bearSignals.Count,
            BullishSignals = bullSignals.Take(6).ToList(),
            BearishSignals = bearSignals.Take(6).ToList(),
        };
    }

    /// <summary>
    /// 掃整個 universe(symbol → K 線),回傳 magnitude ≥ minMagnitude、依 magnitude 由大到小的 Top N。
    /// </summary>
    public static List<ScanResult> ScanUniverse(
        IEnumerable<KeyValuePair<string, List<BarData>>> universe,
        decimal minMagnitude = 2.0m,
        int topN = 10,
        int pivotWindow = 3)
    {
        var results = new List<ScanResult>();
        foreach (var kv in universe)
        {
            var r = ScanSymbol(kv.Key, kv.Value, pivotWindow);
            if (r != null && r.Magnitude >= minMagnitude) results.Add(r);
        }
        return results
            .OrderByDescending(r => r.Magnitude)
            .ThenByDescending(r => r.BullishSignalCount + r.BearishSignalCount)
            .Take(topN)
            .ToList();
    }
}
