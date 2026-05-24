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
    private readonly Dictionary<string, IPerpetualClient> _perpClients;
    private readonly TradingDbStorage _db;
    private readonly ILogger<FillPollerService> _logger;
    private readonly int _pollIntervalSec;

    // 每個 perp exchange 紀錄上次 income poll 的 cursor、避免重撈 + 抓 race window。
    // 首次回補視窗(DB 無 perp-income 紀錄時的 fallback):env FILLPOLLER_PERP_LOOKBACK_MIN、
    // 預設 4320 分(3 天)——夠把近期平倉 realized_pnl 一次補齊;成功寫入後改吃 DB cursor、不再重撈。
    private readonly Dictionary<string, DateTime> _perpIncomeSince = new();
    private readonly TimeSpan _perpFirstLookback;

    public FillPollerService(
        Dictionary<string, IExchangeClient> clients,
        TradingDbStorage db,
        ILogger<FillPollerService> logger,
        int pollIntervalSec = 30,
        Dictionary<string, IPerpetualClient>? perpClients = null)
    {
        _clients = clients;
        _perpClients = perpClients ?? new Dictionary<string, IPerpetualClient>();
        _db = db;
        _logger = logger;
        _pollIntervalSec = Math.Max(10, pollIntervalSec);
        var lookbackMin = int.TryParse(Environment.GetEnvironmentVariable("FILLPOLLER_PERP_LOOKBACK_MIN"), out var lb) && lb > 0
            ? lb : 4320;
        _perpFirstLookback = TimeSpan.FromMinutes(lookbackMin);
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
        await PollSpotFillsAsync(ct);
        await PollPerpIncomeAsync(ct);
    }

    private async Task PollSpotFillsAsync(CancellationToken ct)
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

    /// <summary>
    /// 撈每個 perp exchange 的 realized_pnl income、寫進 trades 表。
    /// 用 _perpIncomeSince[exchange] 當 cursor、SaveTrade 因 tradeId 主鍵冪等、即使 cursor
    /// overlap 也不會雙寫。trade_id 格式 "perp-income-{exchange}-{tradeId}"。
    /// </summary>
    private async Task PollPerpIncomeAsync(CancellationToken ct)
    {
        if (_perpClients.Count == 0) return;

        foreach (var (exchange, client) in _perpClients)
        {
            if (ct.IsCancellationRequested) break;

            // Cursor 來源優先序：
            // 1. 本 process 已記憶的 _perpIncomeSince（hot path）
            // 2. DB 最後一筆 perp-income 時間 + 1ms（broker 重啟後仍有續抓能力）
            // 3. fallback：30 分鐘 lookback（DB 全空 / 首次啟用）
            DateTime since;
            if (_perpIncomeSince.TryGetValue(exchange, out var s))
            {
                since = s;
            }
            else
            {
                var lastDb = _db.GetLatestPerpIncomeTime(exchange);
                since = lastDb?.AddMilliseconds(1) ?? (DateTime.UtcNow - _perpFirstLookback);
                _logger.LogInformation("FillPoller(perp/{Exchange}): bootstrap cursor from {Source} → {Since:o}",
                    exchange, lastDb.HasValue ? "DB" : "fallback-30min", since);
            }

            try
            {
                var incomes = await client.GetIncomeHistoryAsync(symbol: null, sinceUtc: since, ct);
                if (incomes.Count == 0) continue;

                int written = 0;
                DateTime maxTime = since;
                foreach (var inc in incomes)
                {
                    if (inc.Time > maxTime) maxTime = inc.Time;

                    // 只關心 realized_pnl（commission / funding 之後可開另一張表觀察）
                    if (inc.IncomeType != "realized_pnl") continue;
                    if (string.IsNullOrEmpty(inc.TradeId)) continue;

                    _db.SaveTrade(new TradeRecord
                    {
                        TradeId     = $"perp-income-{exchange}-{inc.TradeId}",
                        OrderId     = string.Empty,
                        Symbol      = inc.Symbol,
                        Exchange    = exchange,
                        Side        = "close",   // realized_pnl 只在平倉時產生
                        Quantity    = 0m,        // income 端點沒給 qty、僅能拿 PnL
                        Price       = 0m,
                        Fee         = null,
                        RealizedPnl = inc.Income,
                        ExecutedAt  = inc.Time,
                    });
                    written++;
                }

                // cursor 推進一毫秒、避免下次重撈最後那筆
                _perpIncomeSince[exchange] = maxTime.AddMilliseconds(1);

                if (written > 0)
                    _logger.LogInformation("FillPoller(perp/{Exchange}): wrote {N} realized_pnl rows, cursor={Cursor:o}",
                        exchange, written, _perpIncomeSince[exchange]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FillPoller(perp/{Exchange}): income fetch failed", exchange);
            }
        }
    }
}
