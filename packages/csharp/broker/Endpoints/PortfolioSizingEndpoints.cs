using Broker.Helpers;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Portfolio Sizing 推薦 API(Q1.6 整合、2026-05-27)
///
/// GET /api/v1/portfolio/sizing-recommendation
///   ?equity=347              (預設讀 BingX 真錢權益、若失敗 fallback 該參數)
///   &target_vol=60           (% annualized、預設 60 適合 crypto)
///   &max_cap=20              (per-strategy max %、預設 20)
///   &current_dd=0            (% from peak、預設 0)
///
/// 回傳:每 scanner 推薦 final %、final USDT、Kelly raw/safe、ERC weight、vol/DD scalar
/// **不自動 apply、只 recommend**;手動 SQL UPDATE scanner_legs.budget_total 才生效
/// </summary>
public static class PortfolioSizingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var portfolio = group.MapGroup("/portfolio");

        portfolio.MapGet("/sizing-recommendation", async (
            PortfolioSizingService sizing, BrokerDb db, IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            try
            {
                decimal equity = decimal.TryParse(req.Query["equity"], out var e) ? e : 0m;
                decimal targetVol = decimal.TryParse(req.Query["target_vol"], out var tv) ? tv / 100m : 0.60m;
                decimal maxCap = decimal.TryParse(req.Query["max_cap"], out var mc) ? mc / 100m : 0.20m;
                decimal currentDd = decimal.TryParse(req.Query["current_dd"], out var cd) ? cd / 100m : 0m;

                // 1. 讀 enabled scanner_legs
                List<ScannerLegEntry> legs;
                try
                {
                    legs = db.Query<ScannerLegEntry>(
                        "SELECT * FROM scanner_legs WHERE enabled = 1 ORDER BY id");
                }
                catch (Exception ex)
                {
                    return Results.Ok(ApiResponseHelper.Error($"scanner_legs query failed: {ex.Message}"));
                }
                if (legs.Count == 0)
                    return Results.Ok(ApiResponseHelper.Error("no enabled scanner_legs"));

                var scanners = legs.Select(l => (l.Id, l.Strategy, l.BudgetTotal)).ToList();

                // 2. 如果沒給 equity、嘗試從 bingx account 查
                if (equity <= 0m && registry.HasAvailableWorker("trading.account"))
                {
                    try
                    {
                        var accPayload = JsonSerializer.Serialize(new { exchange = "bingx" });
                        var accResult = await dispatcher.DispatchAsync(
                            BuildRequest("trading.account", "get_balance", accPayload));
                        if (accResult.Success)
                        {
                            var doc = JsonDocument.Parse(accResult.ResultPayload ?? "{}");
                            if (doc.RootElement.TryGetProperty("portfolio_value", out var pvEl))
                                equity = pvEl.GetDecimal();
                            else if (doc.RootElement.TryGetProperty("equity", out var eqEl))
                                equity = eqEl.GetDecimal();
                        }
                    }
                    catch { /* fallback to query param */ }
                }
                if (equity <= 0m) equity = 347m;   // 最終 fallback 用已知數字

                // 3. 拉 BTC bars 算 realized vol
                List<decimal>? btcCloses = null;
                if (registry.HasAvailableWorker("quote.ohlcv"))
                {
                    try
                    {
                        var btcPayload = JsonSerializer.Serialize(new { symbol = "BTCUSDT", interval = "1d", limit = 60 });
                        var btcResult = await dispatcher.DispatchAsync(
                            BuildRequest("quote.ohlcv", "get_bars", btcPayload));
                        if (btcResult.Success)
                        {
                            var doc = JsonDocument.Parse(btcResult.ResultPayload ?? "{}");
                            if (doc.RootElement.TryGetProperty("bars", out var barsArr))
                            {
                                btcCloses = new List<decimal>();
                                foreach (var b in barsArr.EnumerateArray())
                                {
                                    if (b.TryGetProperty("close", out var cl))
                                    {
                                        if (cl.ValueKind == JsonValueKind.Number) btcCloses.Add(cl.GetDecimal());
                                        else if (cl.ValueKind == JsonValueKind.String && decimal.TryParse(cl.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dc)) btcCloses.Add(dc);
                                    }
                                }
                            }
                        }
                    }
                    catch { /* btc fetch fail → vol_scalar=1 fallback */ }
                }

                // 4. 推薦
                var resp = sizing.Recommend(
                    scanners, equity, btcCloses,
                    currentDdPct: currentDd, targetVolPct: targetVol,
                    maxAcceptableDdPct: 0.20m, maxCapPerStrategy: maxCap);

                return Results.Ok(ApiResponseHelper.Success(resp));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResponseHelper.Error($"sizing-recommendation failed: {ex.Message}"));
            }
        });
    }

    private static ApprovedRequest BuildRequest(string capability, string route, string payload)
    {
        return new ApprovedRequest
        {
            RequestId = $"sizing-{Guid.NewGuid():N}"[..16],
            CapabilityId = capability,
            Route = route,
            Payload = payload,
            Scope = "broker.portfolio_sizing",
            PrincipalId = "system",
            Role = "admin",
        };
    }
}
