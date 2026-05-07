using TradingWorker.Models;

namespace TradingWorker.Exchange;

/// <summary>
/// 永續合約交易所介面（跟 spot 的 IExchangeClient 故意分開）。
///
/// 跟 spot 的差別：
///   - GetAccountAsync 回傳 PerpetualAccount（含 margin、equity、unrealized）
///   - GetPositionsAsync 回傳 PerpetualPosition（雙向、含 leverage、liq price）
///   - PlaceOrderAsync 接受 PerpetualOrder（含 PositionSide、Leverage、ReduceOnly）
///   - 多 SetLeverageAsync / GetMarkPriceAsync 兩個方法
///
/// 第一個實作：BingxPerpetualClient（USDT-M perpetual swap）。
/// 之後若要 OKX / Bybit 等只要再實作這個 interface。
/// </summary>
public interface IPerpetualClient
{
    string ExchangeName { get; }
    bool IsDemo { get; }

    Task<PerpetualAccount> GetAccountAsync(CancellationToken ct = default);
    Task<List<PerpetualPosition>> GetPositionsAsync(CancellationToken ct = default);
    Task<PerpetualOrder> PlaceOrderAsync(PerpetualOrder order, CancellationToken ct = default);
    Task<PerpetualOrder> CancelOrderAsync(string symbol, string externalId, CancellationToken ct = default);
    Task<PerpetualOrder?> GetOrderStatusAsync(string symbol, string externalId, CancellationToken ct = default);
    Task<List<PerpetualOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default);

    /// <summary>設定該 symbol 開倉時用的槓桿倍數（BingX 是 per-symbol per-side）</summary>
    Task<bool> SetLeverageAsync(string symbol, string positionSide, int leverage, CancellationToken ct = default);

    /// <summary>取得 mark price（用於計算 PnL / 強平距離）</summary>
    Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default);
}
