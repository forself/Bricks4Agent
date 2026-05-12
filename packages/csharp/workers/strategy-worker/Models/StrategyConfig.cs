using StrategyWorker.Engine;

namespace StrategyWorker.Models;

/// <summary>
/// 策略參數設定。
/// </summary>
public class StrategyConfig
{
    public string Name     { get; set; } = string.Empty;
    public string Symbol   { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Interval { get; set; } = "1d";

    /// <summary>
    /// 大週期 K 線（Batch C+++ 新增、影片重點「大週期優先」）。
    /// 給支援 multi-timeframe 的策略（目前是 HarmonicStrategy）用、確認大週期方向是否一致。
    /// 其他策略可忽略不用。null = caller 沒提供 HTF 資料、跳過 HTF 確認流程。
    /// 慣例：HTF 通常是 LTF 的 4 倍（1h ↔ 4h）或 1d。
    /// </summary>
    public List<BarData>? HtfBars { get; set; }

    /// <summary>HTF 的時間級別文字（例如 "4h"、"1d"）。給策略寫 reason 用、不影響運算。</summary>
    public string? HtfInterval { get; set; }

    // SMA Cross
    public int SmaFast { get; set; } = 10;
    public int SmaSlow { get; set; } = 30;

    // RSI
    public int RsiPeriod       { get; set; } = 14;
    public decimal RsiOversold  { get; set; } = 30;
    public decimal RsiOverbought { get; set; } = 70;

    // MACD
    public int MacdFast   { get; set; } = 12;
    public int MacdSlow   { get; set; } = 26;
    public int MacdSignal { get; set; } = 9;

    // 通用
    public int BarLimit { get; set; } = 100;

    /// <summary>
    /// 策略特定參數（key 用 snake_case 跟 ParamSpec 對齊）。
    /// 想用就由 caller 塞進來、策略內部 GetParam 拿；沒設則 fallback 到本 class 上面的具名欄位。
    /// 之後加新策略不必再改這個 class。
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }

    public T? GetParam<T>(string key, T? fallback = default)
    {
        if (Params == null || !Params.TryGetValue(key, out var v) || v == null) return fallback;
        try
        {
            // 從 JSON deserialization 進來常是 long / double / string
            if (v is T tv) return tv;
            return (T)Convert.ChangeType(v, typeof(T));
        }
        catch { return fallback; }
    }
}
