using System.Collections.Concurrent;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 自動交易背景服務。
///
/// 流程：拉 K 線 → 策略分析 → 風控檢查 → 下單
/// 可透過 API 動態新增/移除/暫停監控的 symbol。
/// </summary>
public class AutoTraderService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<AutoTraderService> _logger;

    private readonly ConcurrentDictionary<string, WatchItem> _watchList = new();
    private readonly ConcurrentQueue<TradeLog> _tradeLog = new();
    private const int MaxLogEntries = 200;

    private int _intervalSeconds = 300; // 預設 5 分鐘
    private bool _enabled = false;

    public AutoTraderService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        ILogger<AutoTraderService> logger)
    {
        _dispatcher = dispatcher;
        _registry   = registry;
        _logger     = logger;
    }

    // ── 外部控制 API ────────────────────────────────────────────────

    public bool IsEnabled => _enabled;
    public int IntervalSeconds => _intervalSeconds;
    public IReadOnlyDictionary<string, WatchItem> WatchList => _watchList;
    public IEnumerable<TradeLog> RecentLogs => _tradeLog.ToArray().Take(MaxLogEntries);

    public void Enable() { _enabled = true; _logger.LogInformation("AutoTrader ENABLED"); }
    public void Disable() { _enabled = false; _logger.LogInformation("AutoTrader DISABLED"); }
    public void SetInterval(int seconds) { _intervalSeconds = Math.Max(60, seconds); }

    public void AddWatch(string symbol, string exchange, string strategy = "composite", decimal quantity = 1)
    {
        var key = $"{exchange}:{symbol}";
        _watchList[key] = new WatchItem
        {
            Symbol = symbol, Exchange = exchange, Strategy = strategy,
            Quantity = quantity, Active = true,
        };
        _logger.LogInformation("AutoTrader: watching {Key} strategy={Strategy} qty={Qty}", key, strategy, quantity);
    }

    public bool RemoveWatch(string symbol, string exchange)
    {
        return _watchList.TryRemove($"{exchange}:{symbol}", out _);
    }

    public void PauseWatch(string symbol, string exchange)
    {
        if (_watchList.TryGetValue($"{exchange}:{symbol}", out var item))
            item.Active = false;
    }

    public void ResumeWatch(string symbol, string exchange)
    {
        if (_watchList.TryGetValue($"{exchange}:{symbol}", out var item))
            item.Active = true;
    }

    // ── 主迴圈 ──────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AutoTrader service started (disabled by default, use API to enable)");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct);
            }
            catch (OperationCanceledException) { break; }

            if (!_enabled || _watchList.IsEmpty) continue;

            foreach (var (key, item) in _watchList)
            {
                if (!item.Active || ct.IsCancellationRequested) continue;

                try
                {
                    await ProcessSymbolAsync(item, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AutoTrader error processing {Key}", key);
                    AddLog(item, "error", $"Exception: {ex.Message}");
                }
            }
        }
    }

    private async Task ProcessSymbolAsync(WatchItem item, CancellationToken ct)
    {
        var symbol   = item.Symbol;
        var exchange = item.Exchange;

        // Step 1: 拉 K 線
        if (!_registry.HasAvailableWorker("quote.ohlcv"))
        {
            AddLog(item, "skip", "quote-worker not connected");
            return;
        }

        var barsPayload = JsonSerializer.Serialize(new { symbol, interval = "1d", limit = 100 });
        var barsResult = await _dispatcher.DispatchAsync(BuildRequest("quote.ohlcv", "get_bars", barsPayload));
        if (!barsResult.Success)
        {
            AddLog(item, "skip", $"Failed to get bars: {barsResult.ErrorMessage}");
            return;
        }

        var barsDoc = JsonDocument.Parse(barsResult.ResultPayload ?? "{}");
        if (!barsDoc.RootElement.TryGetProperty("bars", out var barsArr) || barsArr.GetArrayLength() < 30)
        {
            AddLog(item, "skip", $"Not enough bars: {barsArr.GetArrayLength()}");
            return;
        }

        // Step 2: 策略分析
        if (!_registry.HasAvailableWorker("strategy.signal"))
        {
            AddLog(item, "skip", "strategy-worker not connected");
            return;
        }

        var signalPayload = JsonSerializer.Serialize(new
        {
            strategy = item.Strategy,
            symbol,
            exchange,
            interval = "1d",
            bars = barsArr,
        });
        var signalResult = await _dispatcher.DispatchAsync(BuildRequest("strategy.signal", "evaluate", signalPayload));
        if (!signalResult.Success)
        {
            AddLog(item, "skip", $"Strategy failed: {signalResult.ErrorMessage}");
            return;
        }

        var signal = JsonDocument.Parse(signalResult.ResultPayload ?? "{}").RootElement;
        var action = signal.TryGetProperty("action", out var a) ? a.GetString() ?? "hold" : "hold";
        var confidence = signal.TryGetProperty("confidence", out var c) ? c.GetDecimal() : 0;
        var reason = signal.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

        item.LastSignal = action;
        item.LastConfidence = confidence;
        item.LastCheck = DateTime.UtcNow;

        if (action == "hold")
        {
            AddLog(item, "hold", $"Confidence={confidence:P0}. {TruncateReason(reason)}");
            return;
        }

        // 信心度門檻
        if (confidence < 0.6m)
        {
            AddLog(item, "skip", $"Signal={action} but confidence {confidence:P0} < 60% threshold");
            return;
        }

        // Step 3: 取得價格估算
        decimal price = 0;
        if (signal.TryGetProperty("indicators", out var indicators))
        {
            foreach (var prop in indicators.EnumerateObject())
            {
                if (prop.Name.EndsWith(".price") || prop.Name == "price")
                {
                    price = prop.Value.GetDecimal();
                    break;
                }
            }
        }

        // Step 4: 風控檢查
        if (_registry.HasAvailableWorker("risk.check") && price > 0)
        {
            var riskPayload = JsonSerializer.Serialize(new
            {
                symbol, exchange, side = action,
                quantity = item.Quantity, price,
                portfolio = new { cash = 0, portfolio_value = 0, day_pnl = 0, peak_value = 0, daily_trade_count = 0, positions = Array.Empty<object>() }
            });

            // 嘗試先取得真正的帳戶資訊
            if (_registry.HasAvailableWorker("trading.account"))
            {
                var accResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_account",
                    JsonSerializer.Serialize(new { exchange })));
                var posResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_positions",
                    JsonSerializer.Serialize(new { exchange })));

                if (accResult.Success && posResult.Success)
                {
                    var acc = JsonDocument.Parse(accResult.ResultPayload ?? "{}").RootElement;
                    var pos = JsonDocument.Parse(posResult.ResultPayload ?? "{}").RootElement;

                    riskPayload = JsonSerializer.Serialize(new
                    {
                        symbol, exchange, side = action,
                        quantity = item.Quantity, price,
                        portfolio = new
                        {
                            cash = acc.TryGetProperty("cash", out var cash) ? cash.GetDecimal() : 0,
                            portfolio_value = acc.TryGetProperty("portfolio_value", out var pv) ? pv.GetDecimal() : 0,
                            day_pnl = acc.TryGetProperty("day_pnl", out var dp) ? dp.GetDecimal() : 0,
                            peak_value = acc.TryGetProperty("portfolio_value", out var pk) ? pk.GetDecimal() : 0,
                            daily_trade_count = 0,
                            positions = pos.TryGetProperty("positions", out var posArr2) ? posArr2 : JsonDocument.Parse("[]").RootElement,
                        }
                    });
                }
            }

            var riskResult = await _dispatcher.DispatchAsync(BuildRequest("risk.check", "pre_order", riskPayload));
            if (riskResult.Success)
            {
                var riskDoc = JsonDocument.Parse(riskResult.ResultPayload ?? "{}").RootElement;
                var passed = riskDoc.TryGetProperty("passed", out var p) && p.GetBoolean();
                var orderAction = riskDoc.TryGetProperty("order_action", out var oa) ? oa.GetString() : "reject";

                if (!passed && orderAction == "reject")
                {
                    AddLog(item, "blocked", "Risk check rejected order");
                    return;
                }

                if (orderAction == "reduce" && riskDoc.TryGetProperty("adjusted_qty", out var aq))
                {
                    item.Quantity = aq.GetDecimal();
                    AddLog(item, "adjusted", $"Risk reduced qty to {item.Quantity}");
                }
            }
        }

        // Step 5: 下單
        if (!_registry.HasAvailableWorker("trading.order"))
        {
            AddLog(item, "skip", "trading-worker not connected");
            return;
        }

        var orderPayload = JsonSerializer.Serialize(new
        {
            exchange, symbol, side = action,
            quantity = item.Quantity, order_type = "market",
        });

        var orderResult = await _dispatcher.DispatchAsync(BuildRequest("trading.order", "place_order", orderPayload));
        if (orderResult.Success)
        {
            var order = JsonDocument.Parse(orderResult.ResultPayload ?? "{}").RootElement;
            var orderId = order.TryGetProperty("order_id", out var oid) ? oid.GetString() : "?";
            var status = order.TryGetProperty("status", out var st) ? st.GetString() : "?";
            AddLog(item, action, $"ORDER PLACED: {orderId} {action} {item.Quantity} {symbol} @ market → {status}");
            _logger.LogInformation("AutoTrader: {Action} {Qty} {Symbol} on {Exchange} → {Status}",
                action, item.Quantity, symbol, exchange, status);
        }
        else
        {
            AddLog(item, "error", $"Order failed: {orderResult.ErrorMessage}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ApprovedRequest BuildRequest(string capabilityId, string route, string payload = "{}")
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId, Route = route, Payload = payload,
            Scope = "{}", PrincipalId = "system", TaskId = "auto-trader", SessionId = "auto-trader"
        };

    private void AddLog(WatchItem item, string action, string message)
    {
        _tradeLog.Enqueue(new TradeLog
        {
            Symbol = item.Symbol, Exchange = item.Exchange,
            Action = action, Message = message,
        });
        while (_tradeLog.Count > MaxLogEntries) _tradeLog.TryDequeue(out _);
    }

    private static string TruncateReason(string reason)
        => reason.Length > 120 ? reason[..120] + "…" : reason;
}

// ── Models ──────────────────────────────────────────────────────────

public class WatchItem
{
    public string Symbol   { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Strategy { get; set; } = "composite";
    public decimal Quantity { get; set; } = 1;
    public bool Active     { get; set; } = true;
    public string? LastSignal    { get; set; }
    public decimal LastConfidence { get; set; }
    public DateTime? LastCheck    { get; set; }
}

public class TradeLog
{
    public string Symbol   { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Action   { get; set; } = "";
    public string Message  { get; set; } = "";
    public DateTime Time   { get; set; } = DateTime.UtcNow;
}
