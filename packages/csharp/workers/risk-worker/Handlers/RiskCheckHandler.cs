using System.Text.Json;
using WorkerSdk;
using RiskWorker.Engine;
using RiskWorker.Models;

namespace RiskWorker.Handlers;

/// <summary>
/// risk.check — 風控檢查。
///
/// Routes:
///   pre_order       — spot 下單前檢查（參數：symbol, exchange, side, quantity, price, portfolio）
///   pre_perp_order  — perp 下單前檢查（參數：symbol, exchange, side, position_side,
///                     quantity, price, leverage, perp: { balance, available_margin, positions[] }）
///                     開倉走規則；平倉（SELL+LONG / BUY+SHORT）永遠放行
///   get_rules       — 列出當前規則
///   set_rules       — 更新規則（參數：rules 陣列）
/// </summary>
public class RiskCheckHandler : ICapabilityHandler
{
    private RiskEngine _engine;
    private List<RiskRule> _rules;
    public string CapabilityId => "risk.check";

    public RiskCheckHandler(List<RiskRule> initialRules)
    {
        _rules  = initialRules;
        _engine = new RiskEngine(_rules);
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var result = route switch
        {
            "pre_order"      => PreOrder(payload),
            "pre_perp_order" => PrePerpOrder(payload),
            "get_rules"      => GetRules(),
            "set_rules"      => SetRules(payload),
            _ => (false, (string?)null, $"Unknown route: {route}")
        };
        return Task.FromResult(result);
    }

    private (bool, string?, string?) PrePerpOrder(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;

        var symbol       = doc.TryGetProperty("symbol",        out var s)   ? s.GetString() ?? ""  : "";
        var exchange     = doc.TryGetProperty("exchange",      out var ex)  ? ex.GetString() ?? "" : "";
        var side         = doc.TryGetProperty("side",          out var sd)  ? sd.GetString() ?? "" : "";
        var positionSide = doc.TryGetProperty("position_side", out var ps)  ? ps.GetString() ?? "" : "";
        var quantity     = doc.TryGetProperty("quantity",      out var q)   ? q.GetDecimal()       : 0;
        var price        = doc.TryGetProperty("price",         out var p)   ? p.GetDecimal()       : 0;
        var leverage     = doc.TryGetProperty("leverage",      out var lv) && lv.TryGetInt32(out var lvi) ? lvi : 1;
        var initialSlPct = doc.TryGetProperty("initial_sl_pct", out var isp) ? isp.GetDecimal()  : 5m;

        if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(positionSide) || quantity <= 0 || price <= 0)
            return (false, null, "Missing required: symbol, position_side, quantity > 0, price > 0");

        var snap = new PerpetualSnapshot();
        if (doc.TryGetProperty("perp", out var perp))
        {
            snap.Balance         = perp.TryGetProperty("balance",          out var b)   ? b.GetDecimal()  : 0;
            snap.AvailableMargin = perp.TryGetProperty("available_margin", out var am)  ? am.GetDecimal() : 0;
            snap.DayPnlPct       = perp.TryGetProperty("day_pnl_pct",      out var dpp) ? dpp.GetDecimal() : 0;
            if (perp.TryGetProperty("positions", out var posArr) && posArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var pe in posArr.EnumerateArray())
                {
                    snap.Positions.Add(new PerpetualPositionInfo
                    {
                        Symbol       = pe.TryGetProperty("symbol",        out var psym) ? psym.GetString() ?? "" : "",
                        Exchange     = pe.TryGetProperty("exchange",      out var pex)  ? pex.GetString()  ?? "" : "",
                        PositionSide = pe.TryGetProperty("position_side", out var pps)  ? pps.GetString()  ?? "" : "",
                        Quantity     = pe.TryGetProperty("quantity",      out var pq)   ? pq.GetDecimal()        : 0,
                        MarkPrice    = pe.TryGetProperty("mark_price",    out var pmk)  ? pmk.GetDecimal()       : 0,
                        Notional     = pe.TryGetProperty("notional",      out var pn)   ? pn.GetDecimal()        : 0,
                        Leverage     = pe.TryGetProperty("leverage",      out var pl) && pl.TryGetInt32(out var pli) ? pli : 1,
                        LiquidationDistancePct = pe.TryGetProperty("liquidation_distance_pct", out var pld) ? pld.GetDecimal() : 0,
                    });
                }
            }
        }

        var checkResult = _engine.CheckPerp(symbol, exchange, side, positionSide, quantity, price, leverage, snap, initialSlPct);

        var json = JsonSerializer.Serialize(new
        {
            passed       = checkResult.Passed,
            order_action = checkResult.OrderAction,
            violations   = checkResult.Violations.Select(v => new
            {
                rule_id   = v.RuleId,
                rule_name = v.RuleName,
                message   = v.Message,
                current   = v.Current,
                limit     = v.Limit,
            }),
            checked_at = checkResult.CheckedAt,
        });

        return (true, json, null);
    }

    private (bool, string?, string?) PreOrder(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;

        var symbol   = doc.TryGetProperty("symbol",   out var s)  ? s.GetString() ?? ""   : "";
        var exchange = doc.TryGetProperty("exchange",  out var ex) ? ex.GetString() ?? ""  : "";
        var side     = doc.TryGetProperty("side",      out var sd) ? sd.GetString() ?? ""  : "";
        var quantity = doc.TryGetProperty("quantity",   out var q)  ? q.GetDecimal()        : 0;
        var price    = doc.TryGetProperty("price",      out var p)  ? p.GetDecimal()        : 0;
        // 給 max_loss_per_trade_pct rule 用、broker 端 protection_config 帶來。沒帶用 5% 預設。
        var initialSlPct = doc.TryGetProperty("initial_sl_pct", out var isp) ? isp.GetDecimal() : 5m;

        if (string.IsNullOrEmpty(symbol) || quantity <= 0 || price <= 0)
            return (false, null, "Missing required: symbol, quantity, price (all > 0)");

        // 解析 portfolio snapshot
        var portfolio = new PortfolioSnapshot();
        if (doc.TryGetProperty("portfolio", out var pf))
        {
            portfolio.Cash            = pf.TryGetProperty("cash",             out var c)   ? c.GetDecimal()   : 0;
            portfolio.PortfolioValue  = pf.TryGetProperty("portfolio_value",  out var pv)  ? pv.GetDecimal()  : 0;
            portfolio.DayPnl          = pf.TryGetProperty("day_pnl",          out var dp)  ? dp.GetDecimal()  : 0;
            portfolio.TotalPnl        = pf.TryGetProperty("total_pnl",        out var tp)  ? tp.GetDecimal()  : 0;
            portfolio.PeakValue       = pf.TryGetProperty("peak_value",       out var pk)  ? pk.GetDecimal()  : portfolio.PortfolioValue;
            portfolio.DailyTradeCount = pf.TryGetProperty("daily_trade_count", out var dtc) ? dtc.GetInt32()  : 0;

            if (pf.TryGetProperty("positions", out var posArr) && posArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var pe in posArr.EnumerateArray())
                {
                    portfolio.Positions.Add(new PositionEntry
                    {
                        Symbol        = pe.TryGetProperty("symbol",          out var ps)  ? ps.GetString() ?? "" : "",
                        Exchange      = pe.TryGetProperty("exchange",        out var pex) ? pex.GetString() ?? "" : "",
                        Quantity      = pe.TryGetProperty("quantity",         out var pq)  ? pq.GetDecimal()      : 0,
                        MarketValue   = pe.TryGetProperty("market_value",    out var mv)  ? mv.GetDecimal()      : 0,
                        UnrealizedPnl = pe.TryGetProperty("unrealized_pnl",  out var up)  ? up.GetDecimal()      : 0,
                    });
                }
            }

            // last_trade_by_symbol: { "alpaca:AAPL": "2026-05-02T..." } —— 給 cooldown_seconds 規則
            if (pf.TryGetProperty("last_trade_by_symbol", out var lt) && lt.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in lt.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(prop.Value.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    {
                        portfolio.LastTradeBySymbol[prop.Name] = dt;
                    }
                }
            }
        }

        var checkResult = _engine.Check(symbol, exchange, side, quantity, price, portfolio, initialSlPct);

        var json = JsonSerializer.Serialize(new
        {
            passed       = checkResult.Passed,
            order_action = checkResult.OrderAction,
            adjusted_qty = checkResult.AdjustedQty,
            violations   = checkResult.Violations.Select(v => new
            {
                rule_id   = v.RuleId,
                rule_name = v.RuleName,
                message   = v.Message,
                current   = v.Current,
                limit     = v.Limit,
            }),
            checked_at = checkResult.CheckedAt,
        });

        return (true, json, null);
    }

    private (bool, string?, string?) GetRules()
    {
        var json = JsonSerializer.Serialize(new
        {
            count = _rules.Count,
            rules = _rules.Select(r => new
            {
                rule_id   = r.RuleId,
                name      = r.Name,
                type      = r.Type,
                symbol    = r.Symbol,
                exchange  = r.Exchange,
                threshold = r.Threshold,
                enabled   = r.Enabled,
                scope     = r.Scope,
                @params   = r.Params,
            })
        });
        return (true, json, null);
    }

    private (bool, string?, string?) SetRules(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (false, null, "Missing payload");

        var doc = JsonDocument.Parse(payload).RootElement;
        if (!doc.TryGetProperty("rules", out var rulesArr) || rulesArr.ValueKind != JsonValueKind.Array)
            return (false, null, "Missing 'rules' array");

        var newRules = new List<RiskRule>();
        foreach (var r in rulesArr.EnumerateArray())
        {
            newRules.Add(new RiskRule
            {
                RuleId    = r.TryGetProperty("rule_id",   out var ri) ? ri.GetString() ?? $"r{newRules.Count + 1}" : $"r{newRules.Count + 1}",
                Name      = r.TryGetProperty("name",      out var n)  ? n.GetString() ?? ""  : "",
                Type      = r.TryGetProperty("type",      out var t)  ? t.GetString() ?? ""  : "",
                Symbol    = r.TryGetProperty("symbol",     out var s)  ? s.GetString()        : null,
                Exchange  = r.TryGetProperty("exchange",   out var ex) ? ex.GetString()       : null,
                Threshold = r.TryGetProperty("threshold",  out var th) ? th.GetDecimal()      : 0,
                Enabled   = r.TryGetProperty("enabled",    out var en) ? en.GetBoolean()      : true,
                Scope     = r.TryGetProperty("scope",      out var sc) ? sc.GetString()       : null,
                // params 可能是 string（JSON 字串）或 object（內嵌物件）—— 兩種都接
                Params    = r.TryGetProperty("params",    out var pa)
                              ? (pa.ValueKind == JsonValueKind.String ? pa.GetString() : pa.GetRawText())
                              : null,
            });
        }

        _rules  = newRules;
        _engine = new RiskEngine(_rules);

        return (true, JsonSerializer.Serialize(new { updated = newRules.Count }), null);
    }
}
