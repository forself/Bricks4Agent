using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 策略介面。每個策略接收 K 線資料 + 設定，產出訊號。
/// </summary>
public interface IStrategy
{
    string Name { get; }
    Signal Evaluate(List<BarData> bars, StrategyConfig config);
}

/// <summary>
/// 簡化的 K 線資料（從 broker 查詢結果反序列化）。
/// 避免直接依賴 quote-worker 的 OhlcvBar。
/// </summary>
public class BarData
{
    public DateTime OpenTime { get; set; }
    public decimal Open   { get; set; }
    public decimal High   { get; set; }
    public decimal Low    { get; set; }
    public decimal Close  { get; set; }
    public decimal Volume { get; set; }
}
