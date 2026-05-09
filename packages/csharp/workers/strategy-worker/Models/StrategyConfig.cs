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
