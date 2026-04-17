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
}
