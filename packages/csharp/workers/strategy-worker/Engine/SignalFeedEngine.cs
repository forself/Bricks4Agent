using System;
using System.Collections.Generic;
using System.Linq;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 訊號中心卡片 —— 對單一 symbol 聚合「多維雷達 + 方向 + 信心 + 價格」成一張卡,**不呼叫 LLM**(快又省)。
///
/// 維度(0-100,算不出填 null):
///   signal_strength   : harmonic + price action + SMC 綜合(重用 ScannerEngine 的評分 → magnitude)
///   trend_consistency : 多時框共識(選用,呼叫端給 bullish/bearish/total 計數)
///   momentum          : RSI 偏離 50 + PPO 強度
///   volume            : 近 5 根 vs 近 20 根均量比
///   risk_reward       : ATR 推 SL/TP 的風險報酬比
///   funding           : 資金費率分數(選用,呼叫端給;perp-only)
///
/// 設計沿用 ai-quant-starter2/app/services/signal_feed.py、C# 重寫、接本專案既有指標。
/// 純函式:不抓行情、不快取(快取由呼叫端負責);多時框/資金費率需外部資料 → 設計成選用輸入。
/// </summary>
public static class SignalFeedEngine
{
    public const int MinBars = 30;

    public sealed class Card
    {
        public string Symbol { get; init; } = "";
        public string Direction { get; init; } = "neutral"; // bullish / bearish / neutral
        public int Confidence { get; init; }                 // 0-100
        public int Stars { get; init; }                      // 1-5
        public string Tag { get; init; } = "中性觀望";
        public decimal CurrentPrice { get; init; }
        public decimal? ChangePct { get; init; }
        public decimal? TriggerPrice { get; init; }
        public Dictionary<string, int?> Radar { get; init; } = new();
        public int? AvgWinrate { get; init; }
    }

    public static Card? Build(
        string symbol,
        List<BarData> bars,
        int? mtfBullish = null, int? mtfBearish = null, int? mtfTotal = null,
        int? fundingScore = null)
    {
        if (bars == null || bars.Count < MinBars) return null;

        decimal current = bars[^1].Close;
        decimal? changePct = (bars.Count >= 2 && bars[^2].Close > 0)
            ? Math.Round((current / bars[^2].Close - 1m) * 100m, 2)
            : null;

        // 1) signal_strength + direction —— 重用 ScannerEngine 的 harmonic+PA+SMC 綜合評分
        var scan = ScannerEngine.ScanSymbol(symbol, bars);
        string direction = scan?.Direction ?? "neutral";
        int signalStrength = scan == null ? 0 : (int)Math.Clamp(scan.Magnitude * 15m, 0m, 100m);

        var radar = new Dictionary<string, int?>
        {
            ["signal_strength"]   = signalStrength,
            ["trend_consistency"] = TrendConsistency(mtfBullish, mtfBearish, mtfTotal),
            ["momentum"]          = MomentumScore(bars),
            ["volume"]            = VolumeScore(bars),
            ["risk_reward"]       = RiskRewardScore(bars),
            ["funding"]           = fundingScore,
        };

        var valid = radar.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        int? avgWinrate = valid.Count > 0 ? (int)Math.Round(valid.Average()) : null;

        string tag = direction switch
        {
            "bullish" => "看漲機會",
            "bearish" => "看跌警告",
            _ => "中性觀望",
        };

        return new Card
        {
            Symbol = symbol,
            Direction = direction,
            Confidence = signalStrength,
            Stars = ConfidenceToStars(signalStrength),
            Tag = tag,
            CurrentPrice = Math.Round(current, 4),
            ChangePct = changePct,
            TriggerPrice = TriggerPrice(bars, direction),
            Radar = radar,
            AvgWinrate = avgWinrate,
        };
    }

    private static int ConfidenceToStars(int c)
        => c >= 80 ? 5 : c >= 65 ? 4 : c >= 50 ? 3 : c >= 35 ? 2 : 1;

    // RSI 偏離 50(0..50)+ PPO 強度 → 0-100
    private static int? MomentumScore(List<BarData> bars)
    {
        try
        {
            decimal rsi = HarmonicPatterns.CalcRsiAt(bars, bars.Count - 1, 14);
            var ppo = Ppo.Compute(bars);
            decimal rsiDev = Math.Abs(rsi - 50m);
            decimal ppoStrength = ppo == null ? 0m : Math.Min(1m, Math.Abs(ppo.Value) / 2m);
            decimal score = (rsiDev / 50m) * 60m + ppoStrength * 40m;
            return (int)Math.Clamp(Math.Round(score), 0m, 100m);
        }
        catch { return null; }
    }

    // 近 5 根 vs 近 20 根均量 → 0-100
    private static int? VolumeScore(List<BarData> bars)
    {
        if (bars.Count < 20) return null;
        decimal recent = bars.Skip(bars.Count - 5).Average(b => b.Volume);
        decimal avg = bars.Skip(bars.Count - 20).Average(b => b.Volume);
        if (avg <= 0) return null;
        decimal ratio = recent / avg;
        decimal score = ratio >= 2m ? 80m + Math.Min(15m, (ratio - 2m) * 7.5m)
                      : ratio >= 1m ? 50m + (ratio - 1m) * 30m
                      : 50m * ratio;
        return (int)Math.Clamp(Math.Round(score), 0m, 100m);
    }

    // ATR 推 SL(1.5×)/TP(3×)的風險報酬比 → 0-100
    private static int? RiskRewardScore(List<BarData> bars)
    {
        decimal atr = Atr(bars, 14);
        if (atr <= 0) return null;
        decimal rr = (3.0m * atr) / (1.5m * atr); // = 2
        decimal score = 25m + rr * 22m;            // rr=2 → 69
        return (int)Math.Clamp(Math.Round(score), 0m, 100m);
    }

    // 多時框共識度 → 20-95(選用)
    private static int? TrendConsistency(int? bull, int? bear, int? total)
    {
        if (total is not int t || t <= 0) return null;
        int b = bull ?? 0, s = bear ?? 0;
        decimal maxAlign = (decimal)Math.Max(b, s) / t;
        return Math.Clamp((int)Math.Round(maxAlign * 100m), 20, 95);
    }

    private static decimal? TriggerPrice(List<BarData> bars, string direction)
    {
        var recent = bars.Skip(Math.Max(0, bars.Count - 20)).ToList();
        if (recent.Count == 0) return null;
        return direction switch
        {
            "bullish" => Math.Round(recent.Min(b => b.Low) * 1.005m, 4),
            "bearish" => Math.Round(recent.Max(b => b.High) * 0.995m, 4),
            _ => Math.Round(recent.Average(b => b.Close), 4),
        };
    }

    private static decimal Atr(List<BarData> bars, int period)
    {
        if (bars.Count < period + 1) return 0m;
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];
            decimal tr = Math.Max(cur.High - cur.Low,
                         Math.Max(Math.Abs(cur.High - prev.Close), Math.Abs(cur.Low - prev.Close)));
            sum += tr;
        }
        return sum / period;
    }
}
