using System.Text.Json;
using BrokerCore.Trading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 從 BingX public endpoint 拉永續合約規格、每 12h 自動 refresh、灌進 BrokerCore.Trading.SymbolSpecs。
///
/// **直接走 broker → BingX HTTP**、不繞 trading-worker：
///   - contracts endpoint 是 public、不需簽名 / API key
///   - 跳過 worker dispatch 避免在 trading.perpetual capability 沒 register 時 30s timeout
///   - broker 跑 SymbolSpecs cache、trading-worker 是否上線完全不相關
///
/// 失敗就保持上次快取（或 fallback 到 SymbolSpecs 硬編表）、不阻塞 broker 啟動。
/// 觸發來源：BackgroundService 啟動 + 12h interval。也提供 RefreshNowAsync 給 admin endpoint。
/// </summary>
public class SymbolSpecsService : BackgroundService
{
    private readonly IHttpClientFactory? _httpFactory;
    private readonly HttpClient _http;
    private readonly ILogger<SymbolSpecsService> _logger;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(12);
    private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(20);
    private readonly string _exchange;
    private readonly string _bingxBaseUrl;

    public SymbolSpecsService(ILogger<SymbolSpecsService> logger, IHttpClientFactory? httpFactory = null)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _http = _httpFactory?.CreateClient("symbol-specs") ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(10);
        _exchange = Environment.GetEnvironmentVariable("SYMBOL_SPECS_EXCHANGE") ?? "bingx";

        // BingX VST demo vs live 走的合約規格是一致的（只是帳戶端不同），用 production endpoint 即可
        _bingxBaseUrl = Environment.GetEnvironmentVariable("BINGX_PUBLIC_BASE_URL") ?? "https://open-api.bingx.com";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 給 broker / cron 一點 warmup time、避免一啟動就打外網
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
        if (!_exchange.Equals("bingx", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("SymbolSpecs: exchange={Exchange} not supported by direct fetch (only bingx)", _exchange);
            return (false, 0, $"exchange '{_exchange}' direct fetch not supported");
        }

        try
        {
            var resp = await _http.GetAsync($"{_bingxBaseUrl}/openApi/swap/v2/quote/contracts", ct);
            if (!resp.IsSuccessStatusCode)
                return (false, 0, $"http {(int)resp.StatusCode}");

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json).RootElement;
            if (!doc.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
            {
                var msg = doc.TryGetProperty("msg", out var m) ? m.GetString() : "?";
                return (false, 0, $"BingX code={(codeEl.ValueKind == JsonValueKind.Number ? codeEl.GetInt32().ToString() : "?")} msg={msg}");
            }
            if (!doc.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return (false, 0, "no data array");

            var entries = new List<(string, SymbolSpecs.Spec)>();
            foreach (var c in data.EnumerateArray())
            {
                // status: 1 = trading, 其他 = 已下架/暫停。用 status 過濾、避免把下架的東西放進 cache
                var trading = !c.TryGetProperty("status", out var st) || (st.ValueKind == JsonValueKind.Number && st.GetInt32() == 1);
                if (!trading) continue;

                var sym = c.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(sym)) continue;

                // BingX 欄位：tradeMinQuantity / tradeMinUSDT / quantityPrecision / pricePrecision / maxLongLeverage / maxShortLeverage
                var qtyPrecision = c.TryGetProperty("quantityPrecision", out var qp) && qp.TryGetInt32(out var qpI) ? qpI : 4;
                var qtyStep = (decimal)Math.Pow(10, -qtyPrecision);
                int? pricePrecision = c.TryGetProperty("pricePrecision", out var pp) && pp.TryGetInt32(out var ppI) ? ppI : null;
                var maxLong  = c.TryGetProperty("maxLongLeverage",  out var mll) && mll.TryGetInt32(out var ml)  ? ml  : 0;
                var maxShort = c.TryGetProperty("maxShortLeverage", out var msl) && msl.TryGetInt32(out var ms)  ? ms  : 0;
                var maxLev = Math.Max(maxLong, maxShort);
                if (maxLev <= 0) maxLev = 50;

                entries.Add((sym, new SymbolSpecs.Spec
                {
                    MinQty         = GetDecimal(c, "tradeMinQuantity"),
                    QtyStep        = qtyStep,
                    MinNotional    = GetDecimal(c, "tradeMinUSDT"),
                    MaxLeverage    = maxLev,
                    PricePrecision = pricePrecision,
                }));
            }

            SymbolSpecs.ReplaceCache(_exchange, entries);
            _logger.LogInformation("SymbolSpecs refreshed: exchange={Exchange} count={Count}", _exchange, entries.Count);
            return (true, entries.Count, null);
        }
        catch (TaskCanceledException)
        {
            return (false, 0, "timeout (10s)");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
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
