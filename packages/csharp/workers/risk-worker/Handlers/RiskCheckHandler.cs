using System.Text.Json;
using WorkerSdk;
using RiskWorker.Engine;
using RiskWorker.Models;

namespace RiskWorker.Handlers;

/// <summary>
/// risk.check — 風控檢查。
///
/// Routes:
///   pre_order  — 下單前檢查（參數：symbol, exchange, side, quantity, price, portfolio）
///   get_rules  — 列出當前規則
///   set_rules  — 更新規則（參數：rules 陣列）
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
            "pre_order" => PreOrder(payload),
            "get_rules" => GetRules(),
            "set_rules" => SetRules(payload),
            _ => (false, (string?)null, $"Unknown route: {route}")
        };
        return Task.FromResult(result);
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
        }

        var checkResult = _engine.Check(symbol, exchange, side, quantity, price, portfolio);

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
            });
        }

        _rules  = newRules;
        _engine = new RiskEngine(_rules);

        return (true, JsonSerializer.Serialize(new { updated = newRules.Count }), null);
    }
}
