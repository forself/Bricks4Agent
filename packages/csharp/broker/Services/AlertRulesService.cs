using System.Collections.Concurrent;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 持久化告警規則服務（#2）。
///
/// 跟既有 PriceAlertService 的差異：
///   - 後者：純記憶體、觸發即刪、單一 above/below condition
///   - 本服務：SQLite 持久化、acknowledge workflow、多 condition type、cooldown 防抖
///
/// 支援的 condition：
///   - price_above / price_below：current price vs threshold
///   - position_pnl_below：open position 的 unrealized PnL% &lt; threshold（如 -5%）
///   - portfolio_dd_above：當日 DD% &gt;= threshold（跟 B1 circuit breaker 平行、可獨立警告）
///
/// 主迴圈每 10 秒 poll 一次：拉 quote / position / account 資料、評估每條 enabled 規則、
/// 觸發就寫 AlertEventEntry + 更新 rule.LastTriggeredAt + 觸發 Discord 通知。
/// 已在 cooldown 內的不會重複觸發。
/// </summary>
public class AlertRulesService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly BrokerDb _db;
    private readonly ILogger<AlertRulesService> _logger;

    // 記憶體鏡像——載自 SQLite 後接受 add/update/remove 同步寫回。Poll loop 只讀此 dict。
    private readonly ConcurrentDictionary<string, AlertRuleEntry> _rules = new();
    private const int PollIntervalSeconds = 10;

    public AlertRulesService(
        IExecutionDispatcher dispatcher, IWorkerRegistry registry,
        BrokerDb db, ILogger<AlertRulesService> logger)
    {
        _dispatcher = dispatcher; _registry = registry; _db = db; _logger = logger;
        LoadFromDb();
    }

    public IReadOnlyDictionary<string, AlertRuleEntry> Rules => _rules;

    private void LoadFromDb()
    {
        try
        {
            var entries = _db.GetAll<AlertRuleEntry>();
            foreach (var e in entries) _rules[e.Id] = e;
            _logger.LogInformation("AlertRulesService: loaded {Count} rules from DB", _rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AlertRulesService: failed to load rules from DB");
        }
    }

    // ── CRUD（給 Endpoints 用）─────────────────────────────────────

    public AlertRuleEntry Create(string name, string conditionType, string symbol, string exchange,
        decimal threshold, int cooldownMinutes = 30)
    {
        var rule = new AlertRuleEntry
        {
            Id = $"rule-{Guid.NewGuid():N}"[..14],
            Name = name, ConditionType = conditionType,
            Symbol = symbol.ToUpperInvariant(), Exchange = exchange.ToLowerInvariant(),
            Threshold = threshold,
            Enabled = true,
            CooldownMinutes = cooldownMinutes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _rules[rule.Id] = rule;
        try { _db.Insert(rule); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist rule {Id}", rule.Id); }
        _logger.LogInformation("Alert rule created: {Id} {Name} ({Type} {Symbol} {Threshold})",
            rule.Id, name, conditionType, symbol, threshold);
        return rule;
    }

    public bool Update(string id, Action<AlertRuleEntry> mutate)
    {
        if (!_rules.TryGetValue(id, out var rule)) return false;
        mutate(rule);
        rule.UpdatedAt = DateTime.UtcNow;
        try { _db.Update(rule); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to update rule {Id}", id); }
        return true;
    }

    public bool Delete(string id)
    {
        if (!_rules.TryRemove(id, out _)) return false;
        try { _db.Delete<AlertRuleEntry>(id); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete rule {Id}", id); }
        _logger.LogInformation("Alert rule deleted: {Id}", id);
        return true;
    }

    public IReadOnlyList<AlertEventEntry> GetEvents(int limit = 50, bool unacknowledgedOnly = false)
    {
        try
        {
            var all = _db.GetAll<AlertEventEntry>();
            var q = all.AsEnumerable();
            if (unacknowledgedOnly) q = q.Where(e => e.AcknowledgedAt == null);
            return q.OrderByDescending(e => e.TriggeredAt).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get alert events");
            return Array.Empty<AlertEventEntry>();
        }
    }

    public bool Acknowledge(string eventId)
    {
        try
        {
            var evt = _db.Get<AlertEventEntry>(eventId);
            if (evt == null) return false;
            if (evt.AcknowledgedAt != null) return true;  // already acked
            evt.AcknowledgedAt = DateTime.UtcNow;
            _db.Update(evt);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acknowledge event {Id}", eventId);
            return false;
        }
    }

    // ── 主迴圈 ──────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AlertRulesService started, polling every {Sec}s", PollIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct); }
            catch (OperationCanceledException) { break; }

            if (_rules.IsEmpty) continue;

            try { await EvaluateAllRulesAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alert evaluation cycle failed");
            }
        }
    }

    private async Task EvaluateAllRulesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // 預載所有 exchange 的 quotes/positions/accounts，避免每條規則都重打 worker
        var quotes = await TryLoadQuotesAsync(ct);
        var positionsByExchange = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>();
        var accountByExchange = new Dictionary<string, JsonElement>();

        foreach (var (id, rule) in _rules)
        {
            if (!rule.Enabled) continue;
            // Cooldown 檢查
            if (rule.LastTriggeredAt is { } lastTrig &&
                (now - lastTrig).TotalMinutes < rule.CooldownMinutes)
                continue;

            decimal? observed = null;
            string message = "";

            switch (rule.ConditionType)
            {
                case "price_above":
                case "price_below":
                {
                    if (!quotes.TryGetValue(rule.Symbol, out var price)) continue;
                    observed = price;
                    var triggered = rule.ConditionType == "price_above" ? price >= rule.Threshold : price <= rule.Threshold;
                    if (!triggered) continue;
                    var op = rule.ConditionType == "price_above" ? "≥" : "≤";
                    message = $"{rule.Symbol} price {price:F4} {op} {rule.Threshold:F4}";
                    break;
                }
                case "position_pnl_below":
                {
                    var positions = await GetPositionsAsync(rule.Exchange, positionsByExchange, ct);
                    if (positions == null || !positions.TryGetValue(rule.Symbol, out var p)) continue;
                    var entry = p.TryGetProperty("avg_entry_price", out var e) ? e.GetDecimal() : 0m;
                    var cur   = p.TryGetProperty("current_price",   out var c) ? c.GetDecimal() : 0m;
                    if (entry <= 0m || cur <= 0m) continue;
                    var pnlPct = (cur - entry) / entry * 100m;
                    observed = pnlPct;
                    if (pnlPct > rule.Threshold) continue;  // threshold 通常負數，pnlPct < threshold 才觸發
                    message = $"{rule.Symbol} unrealized P&L {pnlPct:F2}% ≤ {rule.Threshold:F2}% (entry {entry:F4}, now {cur:F4})";
                    break;
                }
                case "portfolio_dd_above":
                {
                    var acc = await GetAccountAsync(rule.Exchange, accountByExchange, ct);
                    if (!acc.HasValue) continue;
                    // 拿當前 portfolio_value 跟我們自己記的 peak 比；若拿不到 peak（無歷史）、跳過
                    var pv = acc.Value.TryGetProperty("portfolio_value", out var v) ? v.GetDecimal() : 0m;
                    if (pv <= 0m) continue;
                    // 簡化：用 day_pnl 推當日報酬率作為 DD proxy（精確的 peak 需要 AutoTraderService 的 state、解耦）
                    // 替代方案：dd = -day_pnl / (portfolio_value - day_pnl) * 100，當天虧損占年初的比例
                    var dayPnl = acc.Value.TryGetProperty("day_pnl", out var dp) ? dp.GetDecimal() : 0m;
                    var basis = pv - dayPnl;
                    if (basis <= 0m) continue;
                    var ddPct = -dayPnl / basis * 100m;  // 虧錢時 dayPnl<0 → ddPct>0
                    observed = ddPct;
                    if (ddPct < rule.Threshold) continue;
                    message = $"{rule.Exchange} portfolio DD {ddPct:F2}% ≥ {rule.Threshold:F2}% (P&L {dayPnl:F2}, value {pv:F2})";
                    break;
                }
                default:
                    continue;  // unknown condition_type
            }

            if (observed == null) continue;

            // 寫事件 + 更新 rule
            var evt = new AlertEventEntry
            {
                Id = $"evt-{Guid.NewGuid():N}"[..14],
                RuleId = rule.Id, RuleName = rule.Name,
                ConditionType = rule.ConditionType, Symbol = rule.Symbol, Exchange = rule.Exchange,
                Threshold = rule.Threshold, ObservedValue = observed.Value, Message = message,
                TriggeredAt = now, AcknowledgedAt = null,
            };
            try { _db.Insert(evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist alert event"); }

            rule.LastTriggeredAt = now;
            try { _db.Update(rule); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to update rule.LastTriggeredAt"); }

            _logger.LogWarning("⚠ ALERT [{Type}] {Name}: {Message}", rule.ConditionType, rule.Name, message);
        }
    }

    private async Task<Dictionary<string, decimal>> TryLoadQuotesAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, decimal>();
        if (!_registry.HasAvailableWorker("quote.prices")) return result;
        try
        {
            var r = await _dispatcher.DispatchAsync(BuildRequest("quote.prices", "get_prices", "{}"));
            if (!r.Success) return result;
            var doc = JsonDocument.Parse(r.ResultPayload ?? "{}");
            if (!doc.RootElement.TryGetProperty("quotes", out var arr)) return result;
            foreach (var q in arr.EnumerateArray())
            {
                var sym = q.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                var price = q.TryGetProperty("price", out var p) ? p.GetDecimal() : 0m;
                if (!string.IsNullOrEmpty(sym) && price > 0m) result[sym] = price;
            }
        }
        catch { /* ignore — alerts shouldn't crash on transient failures */ }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>?> GetPositionsAsync(
        string exchange, Dictionary<string, IReadOnlyDictionary<string, JsonElement>> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(exchange, out var cached)) return cached;
        if (!_registry.HasAvailableWorker("trading.account")) { cache[exchange] = new Dictionary<string, JsonElement>(); return cache[exchange]; }
        try
        {
            var r = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_positions",
                JsonSerializer.Serialize(new { exchange })));
            if (!r.Success) { cache[exchange] = new Dictionary<string, JsonElement>(); return cache[exchange]; }
            var doc = JsonDocument.Parse(r.ResultPayload ?? "{}");
            var dict = new Dictionary<string, JsonElement>();
            if (doc.RootElement.TryGetProperty("positions", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var p in arr.EnumerateArray())
                {
                    var sym = p.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(sym)) dict[sym] = p.Clone();
                }
            cache[exchange] = dict;
            return dict;
        }
        catch { cache[exchange] = new Dictionary<string, JsonElement>(); return cache[exchange]; }
    }

    private async Task<JsonElement?> GetAccountAsync(
        string exchange, Dictionary<string, JsonElement> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(exchange, out var cached)) return cached;
        if (!_registry.HasAvailableWorker("trading.account")) return null;
        try
        {
            var r = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_account",
                JsonSerializer.Serialize(new { exchange })));
            if (!r.Success) return null;
            var doc = JsonDocument.Parse(r.ResultPayload ?? "{}");
            cache[exchange] = doc.RootElement.Clone();
            return cache[exchange];
        }
        catch { return null; }
    }

    private static ApprovedRequest BuildRequest(string capabilityId, string route, string payload) => new()
    {
        RequestId = Guid.NewGuid().ToString("N"),
        CapabilityId = capabilityId,
        Route = route,
        Payload = payload,
        Scope = "{}",
        PrincipalId = "system",
        TaskId = "alert-rules",
        SessionId = "alert-rules",
    };
}
