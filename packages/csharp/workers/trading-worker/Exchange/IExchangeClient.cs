using TradingWorker.Models;

namespace TradingWorker.Exchange;

/// <summary>
/// 交易所 API 統一介面。
/// Alpaca（美股）和 Binance（加密貨幣）各自實作。
/// </summary>
public interface IExchangeClient
{
    string ExchangeName { get; }

    Task<TradingAccount> GetAccountAsync(CancellationToken ct = default);
    Task<List<Position>> GetPositionsAsync(CancellationToken ct = default);
    Task<TradingOrder> PlaceOrderAsync(TradingOrder order, CancellationToken ct = default);
    Task<TradingOrder> CancelOrderAsync(string externalId, CancellationToken ct = default);
    Task<TradingOrder?> GetOrderStatusAsync(string externalId, CancellationToken ct = default);
    Task<List<TradeRecord>> GetRecentTradesAsync(string? symbol = null, int limit = 50, CancellationToken ct = default);
}
