using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// Kelly Criterion 倉位建議 API。
///
///   GET /api/v1/risk/kelly?strategy=sma_cross&symbol=AAPL&capital=10000&fraction=0.5
///     → 根據該策略在該 symbol 的歷史勝率算出建議倉位
///
/// Query params:
///   strategy  — 任一已註冊的策略名稱（預設 sma_cross）
///   symbol    — 要算倉位的標的（預設 AAPL）
///   capital   — 可動用的總資金（預設 10000）
///   fraction  — Fractional Kelly 係數 (0.1-1.0，預設 0.5 半 Kelly)
///   limit     — 歷史 K 線數（預設 300）
/// </summary>
public static class KellyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        // 放在 /risk 底下；/risk 原本就在 middleware allowlist
        var risk = group.MapGroup("/risk");

        risk.MapGet("/kelly", async (KellyPositionSizingService svc, HttpRequest req, CancellationToken ct) =>
        {
            var strategy = req.Query.TryGetValue("strategy", out var s) && !string.IsNullOrWhiteSpace(s)
                ? s.ToString() : "sma_cross";
            var symbol = req.Query.TryGetValue("symbol", out var sy) && !string.IsNullOrWhiteSpace(sy)
                ? sy.ToString().ToUpperInvariant() : "AAPL";
            var capital = req.Query.TryGetValue("capital", out var c) && decimal.TryParse(c, out var cd) && cd > 0
                ? cd : 10_000m;
            var fraction = req.Query.TryGetValue("fraction", out var f) && decimal.TryParse(f, out var fd) && fd > 0
                ? fd : 0.5m;
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var li) && li > 0
                ? Math.Min(li, 5000) : 300;

            var result = await svc.SuggestAsync(strategy, symbol, capital, fraction, limit, ct);
            return result.Success
                ? Results.Ok(ApiResponseHelper.Success(result))
                : Results.Ok(ApiResponseHelper.Error(result.Error ?? "unknown"));
        });
    }
}
