using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using BrokerCore.Trading;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 從 trading-worker 拉永續合約規格、每 12h 自動 refresh、灌進 BrokerCore.Trading.SymbolSpecs 動態快取。
///
/// 取代「硬編表年級數據維護」這個成本——新上架的小幣自動跟著進來、下架的會被 trading=false 過濾。
/// 失敗就保持上次的快取（或 fallback 到 SymbolSpecs 內的 hardcoded BingxSpecs）、不阻塞 broker 啟動。
///
/// 觸發來源：BackgroundService 啟動 + 固定 12h interval。也提供 RefreshNowAsync 給 admin endpoint。
/// </summary>
public class SymbolSpecsService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<SymbolSpecsService> _logger;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(12);
    private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(20);
    private readonly string _exchange;

    public SymbolSpecsService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        ILogger<SymbolSpecsService> logger)
    {
        _dispatcher = dispatcher;
        _registry = registry;
        _logger = logger;
        _exchange = Environment.GetEnvironmentVariable("SYMBOL_SPECS_EXCHANGE") ?? "bingx";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 給 trading-worker 一點時間連上、避免 broker 一開機就拉撲空
        try { await Task.Delay(_startupDelay, ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RefreshNowAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "SymbolSpecsService refresh failed"); }

            try { await Task.Delay(_refreshInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<(bool Ok, int Count, string? Error)> RefreshNowAsync(CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.perpetual"))
        {
            _logger.LogDebug("SymbolSpecs: trading-worker not connected, skip refresh");
            return (false, 0, "trading-worker not connected");
        }

        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "trading.perpetual",
            Route = "get_contracts",
            Payload = JsonSerializer.Serialize(new { exchange = _exchange }),
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "symbol-specs-refresh",
            SessionId = "symbol-specs-refresh",
        };

        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success) return (false, 0, result.ErrorMessage ?? "dispatch failed");

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        if (!doc.TryGetProperty("contracts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return (false, 0, "no contracts in response");

        var entries = new List<(string, SymbolSpecs.Spec)>();
        foreach (var c in arr.EnumerateArray())
        {
            // trading=false（已下架 / 暫停）就不入快取——pre-flight 會 fallback 到 hardcoded 或 unknown warning
            var trading = !c.TryGetProperty("trading", out var t) || t.GetBoolean();
            if (!trading) continue;

            var sym = c.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(sym)) continue;

            entries.Add((sym, new SymbolSpecs.Spec
            {
                MinQty       = GetDecimal(c, "min_qty"),
                QtyStep      = GetDecimal(c, "qty_step"),
                MinNotional  = GetDecimal(c, "min_notional"),
                MaxLeverage  = c.TryGetProperty("max_leverage", out var ml) && ml.TryGetInt32(out var mlI) ? mlI : 50,
            }));
        }

        SymbolSpecs.ReplaceCache(_exchange, entries);
        _logger.LogInformation("SymbolSpecs refreshed: exchange={Exchange} count={Count}", _exchange, entries.Count);
        return (true, entries.Count, null);
    }

    private static decimal GetDecimal(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return 0m;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : 0m,
            JsonValueKind.String => decimal.TryParse(el.GetString(), out var d) ? d : 0m,
            _ => 0m,
        };
    }
}
