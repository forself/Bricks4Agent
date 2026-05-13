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
/// 偵測「user 主動劃轉/充值/提領」自動更新 risk anchor。
///
/// 每 5 min 跑一次（可調）：
///   1. fetch live balance（從 trading.perpetual / trading.account get_account）
///   2. last_seen_balance ← 上次 cycle 結束的 balance（risk_anchor_state DB）
///   3. realized_pnl_since_last ← 從 trades 表算 [last_check_at, now] 區間 PnL
///   4. unexplained_delta = (live - last_seen) - realized_pnl
///   5. |unexplained_delta| > threshold (預設 5 USDT) →
///        - 視為 transfer
///        - AutoTrader.UpdateDeclaredCapital(exchange, live)
///        - 寫回 risk_anchor_state
///        - 推 Discord + LINE 通知 user
///
/// 為什麼 unexplained 才算 transfer：
///   - 策略賺 +50 → live-last 是 +50、realized_pnl 也 +50 → unexplained=0 → 不動 anchor ✓
///   - user 充 +200 → live-last 是 +200、realized_pnl=0 → unexplained=+200 → anchor +200 ✓
///   - user 提 -100 → live-last 是 -100、realized_pnl=0 → unexplained=-100 → anchor -100 ✓
///
/// 對 user 之前定的「漲不放寬」原則：
///   - 「漲不放寬」是針對 unrealized profit / 策略 realized_pnl
///   - 「主動撥款」是 user intent 明確、應該更新——這就是 unexplained 偵測的用意
///
/// 為什麼放 broker 端（不在 trading-worker）：
///   - AutoTrader._declaredCapitalByExchange 在 broker process 內、跨 process 同步成本高
///   - balance fetch 走 dispatcher、跟 trading-worker 在哪無關
/// </summary>
public class BalanceAnchorService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly BrokerDb _db;
    private readonly AutoTraderService _autoTrader;
    private readonly DiscordNotificationService _discord;
    private readonly LineNotificationService _line;
    private readonly ILogger<BalanceAnchorService> _logger;

    private readonly TimeSpan _interval;
    private readonly decimal _threshold;
    private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(60);
    private readonly string[] _exchangesToTrack;

    public BalanceAnchorService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        BrokerDb db,
        AutoTraderService autoTrader,
        DiscordNotificationService discord,
        LineNotificationService line,
        ILogger<BalanceAnchorService> logger)
    {
        _dispatcher = dispatcher;
        _registry = registry;
        _db = db;
        _autoTrader = autoTrader;
        _discord = discord;
        _line = line;
        _logger = logger;

        var intervalMin = ParseIntEnv("BALANCE_ANCHOR_INTERVAL_MIN", defaultValue: 5, min: 1, max: 60);
        _interval = TimeSpan.FromMinutes(intervalMin);
        _threshold = ParseDecimalEnv("BALANCE_ANCHOR_THRESHOLD_USDT", defaultValue: 5m, min: 0.5m, max: 10000m);

        var trackedRaw = Environment.GetEnvironmentVariable("BALANCE_ANCHOR_EXCHANGES") ?? "bingx";
        _exchangesToTrack = trackedRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("BalanceAnchorService: interval={Interval} threshold={Threshold} USDT exchanges=[{Ex}]",
            _interval, _threshold, string.Join(",", _exchangesToTrack));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 1) 啟動時把 DB 已有的 anchor 載到 AutoTrader in-memory dict（broker 重啟保留）
        try { LoadPersistedAnchors(); }
        catch (Exception ex) { _logger.LogWarning(ex, "BalanceAnchor: load persisted anchors failed"); }

        try { await Task.Delay(_startupDelay, ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            foreach (var exchange in _exchangesToTrack)
            {
                if (ct.IsCancellationRequested) break;
                try { await CheckExchangeAsync(exchange, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "BalanceAnchor: check {Exchange} failed", exchange); }
            }

            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Manual override：dashboard / admin 直接設新 anchor 值（bypass deposit/withdraw 偵測）。
    /// 同步寫 AutoTrader + DB + 推通知。給「想把過去 unattributed balance 認列進 anchor」用。
    /// </summary>
    public async Task<(decimal Old, decimal New)> SetAnchorManualAsync(
        string exchange, decimal newAnchor, CancellationToken ct = default)
    {
        if (newAnchor < 0m) throw new ArgumentException("anchor must be >= 0");
        var key = exchange.ToLowerInvariant();

        // 1) update in-memory (AutoTrader 立即生效)
        var (oldVal, newVal) = _autoTrader.UpdateDeclaredCapital(key, newAnchor);

        // 2) update DB
        var state = _db.Get<RiskAnchorState>(key);
        if (state == null)
        {
            // 還沒 init 過：用當下 live 當 last_seen，避免下個 cycle 把 (live - 0) 全當 transfer
            var live = await FetchLiveBalanceAsync(key, ct) ?? newAnchor;
            state = new RiskAnchorState
            {
                Exchange = key,
                CurrentAnchor = newAnchor,
                LastSeenBalance = live,
                LastCheckAt = DateTime.UtcNow,
                LastChangeReason = "manual",
                LastChangeAt = DateTime.UtcNow,
            };
            _db.Insert(state);
        }
        else
        {
            state.CurrentAnchor = newAnchor;
            state.LastChangeReason = "manual";
            state.LastChangeAt = DateTime.UtcNow;
            _db.Update(state);
        }

        // 3) push notification
        var sign = newVal - oldVal >= 0m ? "+" : "";
        var emoji = newVal > oldVal ? "📈" : (newVal < oldVal ? "📉" : "🔧");
        var title = $"{emoji} Risk anchor 手動設定 · {key.ToUpper()}";
        var body = $"管理員直接設定（bypass deposit/withdraw 偵測）\n" +
                   $"Anchor: {oldVal:F2} → **{newVal:F2}** USDT（delta {sign}{newVal - oldVal:F2}）";
        var color = newVal >= oldVal ? 0x0ECB81 : 0xF6465D;
        try { await _discord.SendAdHocAsync(title, body, color, ct); } catch { }
        try { await _line.SendAdHocAsync(title, body, level: "info", ct); } catch { }

        return (oldVal, newVal);
    }

    /// <summary>查當前 anchor + 最近一次變動資訊。給 dashboard 顯示用。</summary>
    public RiskAnchorState? GetState(string exchange)
        => _db.Get<RiskAnchorState>(exchange.ToLowerInvariant());

    private void LoadPersistedAnchors()
    {
        var rows = _db.GetAll<RiskAnchorState>();
        foreach (var r in rows)
        {
            if (r.CurrentAnchor <= 0m) continue;
            _autoTrader.UpdateDeclaredCapital(r.Exchange, r.CurrentAnchor);
            _logger.LogInformation("BalanceAnchor: restored {Exchange} anchor={Anchor:F2} (last_change={Reason} @ {When:o})",
                r.Exchange, r.CurrentAnchor, r.LastChangeReason, r.LastChangeAt);
        }
    }

    private async Task CheckExchangeAsync(string exchange, CancellationToken ct)
    {
        // 拉 live balance —— bingx 用 perpetual capability、其他用 spot account
        var liveBalance = await FetchLiveBalanceAsync(exchange, ct);
        if (liveBalance == null)
        {
            _logger.LogDebug("BalanceAnchor: {Exchange} live balance fetch returned null, skip", exchange);
            return;
        }

        // 載入 / 初始化 anchor state
        var state = _db.Get<RiskAnchorState>(exchange.ToLowerInvariant());
        if (state == null)
        {
            // 第一次：用當前 in-memory anchor（從 env 載的）當基準、但 last_seen 用 live、避免下個 cycle 把整筆 live-anchor 當 transfer
            var currentAnchor = _autoTrader.DeclaredCapital.TryGetValue(exchange, out var a) ? a : liveBalance.Value;
            state = new RiskAnchorState
            {
                Exchange = exchange.ToLowerInvariant(),
                CurrentAnchor = currentAnchor,
                LastSeenBalance = liveBalance.Value,
                LastCheckAt = DateTime.UtcNow,
                LastChangeReason = "init",
                LastChangeAt = DateTime.UtcNow,
            };
            _db.Insert(state);
            _logger.LogInformation("BalanceAnchor: initialized {Exchange} anchor={Anchor:F2} live={Live:F2}",
                exchange, currentAnchor, liveBalance.Value);
            return;
        }

        // 算 unexplained_delta
        var rawDelta = liveBalance.Value - state.LastSeenBalance;
        var realizedSinceLast = SumRealizedPnlSince(exchange, state.LastCheckAt);
        var unexplained = rawDelta - realizedSinceLast;

        _logger.LogDebug("BalanceAnchor {Exchange}: live={Live:F2} last_seen={Last:F2} raw_delta={Raw:F2} pnl_since={Pnl:F2} unexplained={Un:F2}",
            exchange, liveBalance.Value, state.LastSeenBalance, rawDelta, realizedSinceLast, unexplained);

        if (Math.Abs(unexplained) >= _threshold)
        {
            var reason = unexplained > 0m ? "deposit" : "withdraw";
            var oldAnchor = state.CurrentAnchor;
            var newAnchor = Math.Max(0m, oldAnchor + unexplained);

            // 1) update in-memory（AutoTrader 立即生效）
            _autoTrader.UpdateDeclaredCapital(exchange, newAnchor);

            // 2) update DB（持久化）
            state.CurrentAnchor = newAnchor;
            state.LastChangeReason = reason;
            state.LastChangeAt = DateTime.UtcNow;
            state.LastSeenBalance = liveBalance.Value;
            state.LastCheckAt = DateTime.UtcNow;
            _db.Update(state);

            // 3) 推通知
            await PushAnchorChangeAsync(exchange, reason, oldAnchor, newAnchor, unexplained, ct);
        }
        else
        {
            // unexplained 在 threshold 內：只 advance cursor、不動 anchor
            state.LastSeenBalance = liveBalance.Value;
            state.LastCheckAt = DateTime.UtcNow;
            _db.Update(state);
        }
    }

    private async Task<decimal?> FetchLiveBalanceAsync(string exchange, CancellationToken ct)
    {
        // bingx 走 trading.perpetual（perp wallet 跟 spot 是分開的）；其他走 trading.account
        var capability = exchange.Equals("bingx", StringComparison.OrdinalIgnoreCase)
            ? "trading.perpetual" : "trading.account";

        if (!_registry.HasAvailableWorker(capability))
        {
            _logger.LogDebug("BalanceAnchor: {Capability} worker unavailable", capability);
            return null;
        }

        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = capability,
            Route = "get_account",
            Payload = JsonSerializer.Serialize(new { exchange }),
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "balance-anchor",
            SessionId = "balance-anchor",
        };
        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success) return null;

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        // perp 端點欄位是 "balance"（PerpetualAccount.Balance）；spot 看 capability handler 對應
        if (doc.TryGetProperty("balance", out var b) && b.ValueKind == JsonValueKind.Number)
            return b.GetDecimal();
        if (doc.TryGetProperty("equity", out var e) && e.ValueKind == JsonValueKind.Number)
            return e.GetDecimal();
        // spot account 的 cash 欄位
        if (doc.TryGetProperty("cash", out var c) && c.ValueKind == JsonValueKind.Number)
            return c.GetDecimal();
        return null;
    }

    private decimal SumRealizedPnlSince(string exchange, DateTime sinceUtc)
    {
        // 走 trading.account/get_trade_history（純讀本地 DB、不打外網）
        if (!_registry.HasAvailableWorker("trading.account")) return 0m;

        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "trading.account",
            Route = "get_trade_history",
            Payload = JsonSerializer.Serialize(new
            {
                exchange,
                limit = 500,
                since = sinceUtc.ToString("o"),
            }),
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "balance-anchor-pnl",
            SessionId = "balance-anchor-pnl",
        };
        // 同步等：interval=5min、worker timeout 30s 仍夠用
        var result = _dispatcher.DispatchAsync(req).GetAwaiter().GetResult();
        if (!result.Success) return 0m;

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        if (!doc.TryGetProperty("trades", out var tradesEl) || tradesEl.ValueKind != JsonValueKind.Array)
            return 0m;

        decimal sum = 0m;
        foreach (var t in tradesEl.EnumerateArray())
        {
            if (!t.TryGetProperty("realized_pnl", out var p) || p.ValueKind != JsonValueKind.Number) continue;
            sum += p.GetDecimal();
        }
        return sum;
    }

    private async Task PushAnchorChangeAsync(
        string exchange, string reason, decimal oldAnchor, decimal newAnchor, decimal unexplained, CancellationToken ct)
    {
        var sign = unexplained >= 0m ? "+" : "";
        var emoji = reason == "deposit" ? "💰" : "💸";
        var title = $"{emoji} Risk anchor 自動更新 · {exchange.ToUpper()}";
        var body = $"偵測到 {reason}（unexplained delta = {sign}{unexplained:F2} USDT）\n" +
                   $"Anchor: {oldAnchor:F2} → **{newAnchor:F2}** USDT\n" +
                   $"Sizing 即時生效：per-trade max = {newAnchor:F2} × dynamic_risk_pct";
        var color = reason == "deposit" ? 0x0ECB81 : 0xF6465D;

        try { await _discord.SendAdHocAsync(title, body, color, ct); } catch { }
        try { await _line.SendAdHocAsync(title, body, level: reason == "deposit" ? "success" : "warning", ct); } catch { }
    }

    private static int ParseIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }

    private static decimal ParseDecimalEnv(string name, decimal defaultValue, decimal min, decimal max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (decimal.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
}
