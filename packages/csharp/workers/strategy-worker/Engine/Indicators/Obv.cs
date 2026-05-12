using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// OBV（On-Balance Volume）— Joseph Granville 的能量潮指標。
///
/// 算法：
///   OBV[0] = 0
///   for i &gt; 0：
///     if Close[i] &gt; Close[i-1]:  OBV[i] = OBV[i-1] + Volume[i]
///     if Close[i] &lt; Close[i-1]:  OBV[i] = OBV[i-1] - Volume[i]
///     if Close[i] == Close[i-1]: OBV[i] = OBV[i-1]
///
/// 解讀：OBV 累積方向反映「資金是否認同價格走勢」。
///   價漲 + OBV 漲：健康趨勢
///   價漲 + OBV 不漲：背離、可能假突破
///   OBV 上穿 EMA：加速流入訊號（朋友 repo 用此判定）
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_obv_trend。
/// </summary>
public static class Obv
{
    public class Result
    {
        public decimal Obv       { get; init; }      // 最後一根的累積值
        public decimal Sma       { get; init; }      // 20-bar SMA of OBV
        public decimal Ema       { get; init; }      // 10-bar EMA of OBV
        public bool    AboveSma  { get; init; }
        public bool    JustCrossedAboveEma { get; init; }   // OBV 由下穿上 EMA（加速流入）
    }

    public static Result? Compute(List<BarData> bars, int smaPeriod = 20, int emaPeriod = 10)
    {
        if (bars == null || bars.Count < smaPeriod + 1) return null;

        var n = bars.Count;
        var obv = new decimal[n];
        obv[0] = 0m;
        for (int i = 1; i < n; i++)
        {
            var diff = bars[i].Close - bars[i - 1].Close;
            // Volume = 0 視為 1（朋友 repo fallback、避免完全沒能量資料時 OBV 完全沒動）
            var vol = bars[i].Volume == 0m ? 1m : bars[i].Volume;
            if (diff > 0m)      obv[i] = obv[i - 1] + vol;
            else if (diff < 0m) obv[i] = obv[i - 1] - vol;
            else                obv[i] = obv[i - 1];
        }

        // SMA over last smaPeriod
        var lastIdx = n - 1;
        decimal sumSma = 0m;
        for (int i = lastIdx - smaPeriod + 1; i <= lastIdx; i++) sumSma += obv[i];
        var sma = sumSma / smaPeriod;

        // EMA(emaPeriod) from start
        var alpha = 2m / (emaPeriod + 1);
        decimal ema = obv[0];
        for (int i = 1; i < n; i++) ema = alpha * obv[i] + (1m - alpha) * ema;
        // 前一根的 EMA、用來判斷剛剛是否上穿
        decimal emaPrev = obv[0];
        for (int i = 1; i < n - 1; i++) emaPrev = alpha * obv[i] + (1m - alpha) * emaPrev;

        var aboveSma = obv[lastIdx] > sma;
        var justCrossedAbove = obv[lastIdx] > ema && (n >= 2 && obv[lastIdx - 1] <= emaPrev);

        return new Result
        {
            Obv      = Math.Round(obv[lastIdx], 4),
            Sma      = Math.Round(sma, 4),
            Ema      = Math.Round(ema, 4),
            AboveSma = aboveSma,
            JustCrossedAboveEma = justCrossedAbove,
        };
    }
}
