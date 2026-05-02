using Microsoft.Extensions.Logging;
using TradingWorker.Exchange;
using TradingWorker.Models;
using TradingWorker.Storage;

namespace TradingWorker.Services;

/// <summary>
/// 持續輪詢仍可能成交的訂單、把交易所當前狀態同步回本地 DB。
///
/// 解決什麼：原本下單後就斷訊——broker 不知道單子有沒有成交，risk engine 的
/// max_daily_trades 規則永遠拿到 0、形同虛設。這個 poller 把「成交」這層觀察
/// 補回來，trades 表會持續累積、daily count 才是真實可用的訊號。
///
/// 設計重點：
///   - 只 poll non-terminal 狀態的單（pending / submitted / partial）
///   - 用 trade_id = "fill-{externalId}" 做 idempotent 寫入，重複 poll 不會雙寫
///   - 任一單失敗只 log warning、不中斷整輪（可能單純是 rate limit / 暫時性錯誤）
///   - poll interval 30s（保守，避免 rate limit；可從 config 調）
/// </summary>
public class FillPollerService
{
    private readonly Dictionary<string, IExchangeClient> _clients;
    private readonly TradingDbStorage _db;
    private readonly ILogger<FillPollerService> _logger;
    private readonly int _pollIntervalSec;

    public FillPollerService(
        Dictionary<string, IExchangeClient> clients,
        TradingDbStorage db,
        ILogger<FillPollerService> logger,
        int pollIntervalSec = 30)
    {
        _clients = clients;
        _db = db;
        _logger = logger;
        _pollIntervalSec = Math.Max(10, pollIntervalSec);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("FillPoller started (interval={Sec}s)", _pollIntervalSec);

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSec), ct); }
            catch (OperationCanceledException) { break; }

            try
            {
                await PollOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FillPoller: unexpected error in poll loop");
            }
        }
    }

    internal async Task PollOnceAsync(CancellationToken ct)
    {
        var openOrders = _db.GetOpenOrders();
        if (openOrders.Count == 0) return;

        foreach (var order in openOrders)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(order.ExternalId)) continue;
            if (!_clients.TryGetValue(order.Exchange, out var client))
            {
                // exchange 沒設定（appsettings 改過？）→ 跳過、留著 DB 紀錄等下次
                continue;
            }

            try
            {
                var fresh = await client.GetOrderStatusAsync(order.ExternalId, ct);
                if (fresh == null) continue;

                var statusChanged = fresh.Status != order.Status;
                var fillProgressed = fresh.FilledQty > order.FilledQty;
                if (!statusChanged && !fillProgressed) continue;

                // 保留本地 OrderId（client_order_id），交易所 GetOrderStatus 可能不回
                fresh.OrderId = order.OrderId;
                fresh.UpdatedAt = DateTime.UtcNow;
                _db.SaveOrder(fresh);

                _logger.LogInformation(
                    "FillPoller: {OrderId} ({Symbol}@{Exchange}) {OldStatus}→{NewStatus} filled={Filled}/{Qty}",
                    fresh.OrderId, fresh.Symbol, fresh.Exchange,
                    order.Status, fresh.Status, fresh.FilledQty, fresh.Quantity);

                // Idempotent trade insert：trade_id="fill-{externalId}" → INSERT OR REPLACE
                if (fresh.Status == "filled" && fresh.FilledQty > 0 && fresh.FilledPrice.HasValue)
                {
                    _db.SaveTrade(new TradeRecord
                    {
                        TradeId = $"fill-{fresh.ExternalId}",
                        OrderId = fresh.OrderId,
                        Symbol = fresh.Symbol,
                        Exchange = fresh.Exchange,
                        Side = fresh.Side,
                        Quantity = fresh.FilledQty,
                        Price = fresh.FilledPrice.Value,
                        ExecutedAt = fresh.FilledAt ?? DateTime.UtcNow,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FillPoller: failed to refresh {OrderId} ({Ext})",
                    order.OrderId, order.ExternalId);
            }
        }
    }
}
