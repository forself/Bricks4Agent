using System.Collections.Concurrent;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 價格告警背景服務。
/// 每 10 秒檢查一次報價，觸發告警後自動移除。
/// </summary>
public class PriceAlertService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<PriceAlertService> _logger;
    private readonly ConcurrentDictionary<string, PriceAlert> _alerts = new();
    private readonly ConcurrentQueue<AlertEvent> _history = new();
    private const int MaxHistory = 100;

    public PriceAlertService(IExecutionDispatcher dispatcher, IWorkerRegistry registry, ILogger<PriceAlertService> logger)
    { _dispatcher = dispatcher; _registry = registry; _logger = logger; }

    public IReadOnlyDictionary<string, PriceAlert> Alerts => _alerts;
    public IEnumerable<AlertEvent> History => _history.ToArray();

    public string AddAlert(string symbol, string condition, decimal targetPrice, string? note = null)
    {
        var id = $"alert-{Guid.NewGuid():N}"[..14];
        _alerts[id] = new PriceAlert { Id = id, Symbol = symbol.ToUpper(), Condition = condition, TargetPrice = targetPrice, Note = note ?? "" };
        _logger.LogInformation("Alert added: {Id} {Symbol} {Condition} {Target}", id, symbol, condition, targetPrice);
        return id;
    }

    public bool RemoveAlert(string id) => _alerts.TryRemove(id, out _);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(10_000, ct); } catch { break; }
            if (_alerts.IsEmpty || !_registry.HasAvailableWorker("quote.prices")) continue;

            try
            {
                var result = await _dispatcher.DispatchAsync(new ApprovedRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"), CapabilityId = "quote.prices",
                    Route = "get_prices", Payload = "{}", Scope = "{}", PrincipalId = "system",
                    TaskId = "alert", SessionId = "alert"
                });

                if (!result.Success) continue;
                var doc = JsonDocument.Parse(result.ResultPayload ?? "{}");
                if (!doc.RootElement.TryGetProperty("quotes", out var quotes)) continue;

                var prices = new Dictionary<string, decimal>();
                foreach (var q in quotes.EnumerateArray())
                {
                    var sym = q.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                    var price = q.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                    if (!string.IsNullOrEmpty(sym)) prices[sym] = price;
                }

                foreach (var (id, alert) in _alerts)
                {
                    if (!prices.TryGetValue(alert.Symbol, out var currentPrice)) continue;
                    bool triggered = alert.Condition switch
                    {
                        "above" => currentPrice >= alert.TargetPrice,
                        "below" => currentPrice <= alert.TargetPrice,
                        _ => false
                    };

                    if (triggered)
                    {
                        _alerts.TryRemove(id, out _);
                        var evt = new AlertEvent { Id = id, Symbol = alert.Symbol, Condition = alert.Condition, TargetPrice = alert.TargetPrice, CurrentPrice = currentPrice, Note = alert.Note };
                        _history.Enqueue(evt);
                        while (_history.Count > MaxHistory) _history.TryDequeue(out _);
                        _logger.LogInformation("ALERT TRIGGERED: {Symbol} {Condition} {Target} (current={Current})", alert.Symbol, alert.Condition, alert.TargetPrice, currentPrice);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Alert check error"); }
        }
    }
}

public class PriceAlert
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Condition { get; set; } = "above"; // "above" | "below"
    public decimal TargetPrice { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AlertEvent
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Condition { get; set; } = "";
    public decimal TargetPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Note { get; set; } = "";
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
}
