using System;
using System.Collections.Generic;

namespace StrategyWorker.Engine;

/// <summary>
/// 持倉決策引擎 —— 對「已開倉位」給出 ADD / HOLD / TRIM / EXIT 建議。
///
/// 補 AutoTrader 的缺口:AutoTrader 管進場 + SL/TP/BE/部分出場,但沒有一個「整體該不該
/// 續抱 / 加減碼 / 出場」的決策層。本引擎吃倉位狀態 + 技術訊號 + 風險 + 選用修正維度,輸出
/// 主決策 + 信心(0-100) + 目標價 + 證據。
///
/// 決策模型(沿用 ai-quant-starter2/app/agents/position_agent.py 的設計、C# 重寫):
///   核心 = 損益區間(pnl%)× 訊號強弱(strong/weak/neutral)× 風險(high?)→ 決策表
///     鐵則:虧損不加碼(average-down 陷阱),負浮損即使強多訊號最多 HOLD。
///   修正 = 多時框共識(-2..+2)+ 基本面(-2..+2)+ 新聞(-1..+1)
///     modifier ≥ +2 → 升一階(往 ADD);≤ -3 → 降一階(往 EXIT);升階比降階寬鬆,避免錯殺。
///
/// 純函式:吃呼叫端算好的訊號/修正分數,本身不抓行情、不算指標,方便單測與整合。
/// </summary>
public static class PositionDecisionEngine
{
    public enum Decision { Exit = 0, Trim = 1, Hold = 2, Add = 3 }

    public sealed class Input
    {
        public string Symbol { get; init; } = "";
        public decimal CostBasis { get; init; }
        public decimal Quantity { get; init; }
        public decimal CurrentPrice { get; init; }
        public string Side { get; init; } = "long";              // long / short
        public decimal TechnicalScore { get; init; }             // -1..1
        public string TechnicalSignal { get; init; } = "neutral"; // bullish / bearish / neutral
        public string RiskLevel { get; init; } = "medium";        // low / medium / high
        public decimal? Atr { get; init; }
        public int? HoldDays { get; init; }

        // 選用修正維度(呼叫端算好餵進來;本專案暫無基本面資料源 → FundamentalScore 預設 0)
        public int MtfBullish { get; init; }
        public int MtfBearish { get; init; }
        public int MtfTotal { get; init; }
        public decimal? NewsScore { get; init; }      // -1..1
        public int FundamentalScore { get; init; }    // -2..2
    }

    public sealed class Result
    {
        public string Symbol { get; init; } = "";
        public Decision Decision { get; init; }
        public Decision BaseDecision { get; init; }
        public int Confidence { get; init; }          // 0-100
        public decimal Pnl { get; init; }
        public decimal PnlPct { get; init; }
        public string SignalStrength { get; init; } = "neutral"; // strong / weak / neutral
        public int ModifierTotal { get; init; }
        public decimal StopLoss { get; init; }
        public decimal TakeProfit1 { get; init; }
        public decimal TakeProfit2 { get; init; }
        public decimal AddPrice { get; init; }
        public string Reason { get; init; } = "";
        public List<string> Evidence { get; init; } = new();
    }

    public static Result Decide(Input p)
    {
        if (p.CostBasis <= 0) throw new ArgumentException("CostBasis must be positive");
        if (p.Quantity <= 0) throw new ArgumentException("Quantity must be positive");
        if (p.CurrentPrice <= 0) throw new ArgumentException("CurrentPrice must be positive");

        bool isShort = p.Side == "short";
        decimal pnlPct, pnl;
        string sig;
        if (isShort)
        {
            pnlPct = (1m - p.CurrentPrice / p.CostBasis) * 100m;
            pnl = (p.CostBasis - p.CurrentPrice) * p.Quantity;
            // 做空:bullish 訊號不利、bearish 有利 → 反轉後再分級,讓 strong=有利
            var flipped = p.TechnicalSignal switch { "bullish" => "bearish", "bearish" => "bullish", _ => p.TechnicalSignal };
            sig = ClassifySignal(flipped, -p.TechnicalScore);
        }
        else
        {
            pnlPct = (p.CurrentPrice / p.CostBasis - 1m) * 100m;
            pnl = (p.CurrentPrice - p.CostBasis) * p.Quantity;
            sig = ClassifySignal(p.TechnicalSignal, p.TechnicalScore);
        }
        bool highRisk = p.RiskLevel == "high";

        // 1) 主決策
        var (baseDecision, baseReason) = BaseDecide(pnlPct, sig, highRisk);

        // 2) 修正維度
        var (mtfScore, mtfNote) = EvalMtf(p.MtfBullish, p.MtfBearish, p.MtfTotal);
        var (newsScore, newsNote) = EvalNews(p.NewsScore);
        int fundScore = Math.Clamp(p.FundamentalScore, -2, 2);
        int modifier = mtfScore + newsScore + fundScore;

        // 3) 套用修正(升階 ≥ +2、降階 ≤ -3,升比降寬鬆)
        var finalDecision = baseDecision;
        var extra = new List<string>();
        if (modifier >= 2 && baseDecision is Decision.Exit or Decision.Trim or Decision.Hold)
        {
            finalDecision = StepUp(baseDecision);
            extra.Add($"修正維度合計 +{modifier},決策上修為 {finalDecision}。");
        }
        else if (modifier <= -3 && baseDecision is Decision.Add or Decision.Hold or Decision.Trim)
        {
            finalDecision = StepDown(baseDecision);
            extra.Add($"修正維度合計 {modifier},決策下修為 {finalDecision}。");
        }

        // 4) 信心(0-100)
        decimal conf = 50m + Math.Abs(pnlPct) * 0.8m;
        if (sig is "strong" or "weak") conf += 10m;
        if (highRisk) conf -= 5m;
        conf += Math.Abs(modifier) * 4m;
        int confidence = (int)Math.Clamp(conf, 0m, 100m);

        // 5) 目標價
        var (sl, tp1, tp2, addPrice) = CalcTargets(p.CostBasis, p.CurrentPrice, p.Atr, p.Side);

        // 6) 證據
        var evidence = new List<string>();
        if (mtfNote != null) evidence.Add($"多時框({mtfScore:+0;-0;0}):{mtfNote}");
        if (newsNote != null) evidence.Add($"新聞面({newsScore:+0;-0;0}):{newsNote}");
        if (fundScore != 0) evidence.Add($"基本面({fundScore:+0;-0;0})");
        if (p.HoldDays is int d) evidence.Add(d >= 365 ? $"持有 {d} 天(長期)" : $"持有 {d} 天");

        return new Result
        {
            Symbol = p.Symbol,
            Decision = finalDecision,
            BaseDecision = baseDecision,
            Confidence = confidence,
            Pnl = Math.Round(pnl, 2),
            PnlPct = Math.Round(pnlPct, 2),
            SignalStrength = sig,
            ModifierTotal = modifier,
            StopLoss = sl,
            TakeProfit1 = tp1,
            TakeProfit2 = tp2,
            AddPrice = addPrice,
            Reason = baseReason + (extra.Count > 0 ? " " + string.Join(" ", extra) : ""),
            Evidence = evidence,
        };
    }

    private static string ClassifySignal(string signal, decimal score)
    {
        if (signal == "bullish" && score >= 0.25m) return "strong";
        if (signal == "bearish" || score <= -0.20m) return "weak";
        return "neutral";
    }

    // 主決策表(損益 × 訊號 × 風險)。鐵則:虧損不加碼。
    private static (Decision, string) BaseDecide(decimal pnlPct, string sig, bool highRisk)
    {
        string p = $"{pnlPct:+0.0;-0.0;0.0}%";
        if (pnlPct >= 20m)
        {
            if (sig == "weak" || highRisk) return (Decision.Trim, $"獲利 {p},訊號轉弱或風險升高,分批減碼鎖利。");
            if (sig == "strong") return (Decision.Hold, $"獲利 {p},趨勢仍強,續抱並用追蹤止損保護獲利。");
            return (Decision.Trim, $"獲利 {p} 已豐厚,技術中性,分批減碼鎖利。");
        }
        if (pnlPct >= 5m)
        {
            if (sig == "weak") return (Decision.Trim, $"獲利 {p} 但技術轉弱,減半倉位降風險。");
            if (!highRisk) return (Decision.Add, $"獲利 {p} 站穩成本上方且無警訊,可順勢加碼。");
            return (Decision.Hold, $"獲利 {p},訊號中性但風險偏高,續抱觀察。");
        }
        if (pnlPct >= 4m)
        {
            if (sig == "strong" && !highRisk) return (Decision.Add, $"獲利 {p} 趨勢轉強,回踩支撐可加碼。");
            if (sig == "weak") return (Decision.Trim, $"獲利 {p} 但技術轉弱,先減半保護。");
            return (Decision.Hold, $"獲利 {p},訊號中性,續抱等更明確訊號。");
        }
        if (pnlPct >= -5m)
        {
            if (pnlPct >= 0m && sig == "strong" && !highRisk) return (Decision.Add, $"獲利 {p} 站穩成本上方且趨勢轉強,可順勢加碼。");
            if (sig == "weak") return (Decision.Trim, $"接近成本({p})但訊號轉弱,減倉降風險。");
            return (Decision.Hold, $"接近成本({p}),續抱觀察(虧損不加碼)。");
        }
        if (pnlPct >= -15m)
        {
            if (sig == "weak") return (Decision.Exit, $"虧損 {p} 且未見止穩,建議停損出場。");
            if (sig == "strong") return (Decision.Hold, $"虧損 {p},技術顯示反彈契機,續抱觀察。");
            return (Decision.Trim, $"虧損 {p},訊號中性偏弱,減半降風險。");
        }
        if (sig == "strong") return (Decision.Hold, $"已大虧 {p} 但顯示反彈訊號,保留小倉等反彈點再決定。");
        return (Decision.Exit, $"已大虧 {p} 且無止穩,建議停損出場保護資金。");
    }

    private static (int, string?) EvalMtf(int bullish, int bearish, int total)
    {
        if (total <= 0) return (0, null);
        if (bullish == total) return (2, $"{bullish}/{total} 時間框架一致偏多");
        if (bearish == total) return (-2, $"{bearish}/{total} 時間框架一致偏空");
        if (bullish >= bearish + 1) return (1, $"{bullish}/{total} 時間框架偏多");
        if (bearish >= bullish + 1) return (-1, $"{bearish}/{total} 時間框架偏空");
        return (0, "時間框架訊號分歧");
    }

    private static (int, string?) EvalNews(decimal? newsScore)
    {
        if (newsScore is not decimal s) return (0, null);
        if (s >= 0.15m) return (1, "新聞面偏多");
        if (s <= -0.15m) return (-1, "新聞面偏空");
        return (0, null);
    }

    private static Decision StepUp(Decision d) => d switch
    {
        Decision.Exit => Decision.Trim,
        Decision.Trim => Decision.Hold,
        Decision.Hold => Decision.Add,
        _ => Decision.Add,
    };

    private static Decision StepDown(Decision d) => d switch
    {
        Decision.Add => Decision.Hold,
        Decision.Hold => Decision.Trim,
        Decision.Trim => Decision.Exit,
        _ => Decision.Exit,
    };

    // long:SL 在成本下方、TP 在上方;short 反向。
    private static (decimal Sl, decimal Tp1, decimal Tp2, decimal Add) CalcTargets(
        decimal cost, decimal current, decimal? atr, string side)
    {
        decimal sl, tp1, tp2, add;
        bool hasAtr = atr is decimal a && a > 0;
        decimal av = atr ?? 0m;
        if (side == "short")
        {
            sl  = hasAtr ? Math.Min(cost * 1.08m, current + 2.0m * av) : cost * 1.08m;
            tp1 = hasAtr ? current - 1.5m * av : current * 0.95m;
            tp2 = hasAtr ? current - 3.0m * av : current * 0.90m;
            add = hasAtr ? current + 1.0m * av : current * 1.03m;
        }
        else
        {
            sl  = hasAtr ? Math.Max(cost * 0.92m, current - 2.0m * av) : cost * 0.92m;
            tp1 = hasAtr ? current + 1.5m * av : current * 1.05m;
            tp2 = hasAtr ? current + 3.0m * av : current * 1.10m;
            add = hasAtr ? current - 1.0m * av : current * 0.97m;
        }
        return (Math.Round(sl, 4), Math.Round(tp1, 4), Math.Round(tp2, 4), Math.Round(add, 4));
    }
}
