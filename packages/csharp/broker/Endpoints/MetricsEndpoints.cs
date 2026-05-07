using System.Diagnostics;
using System.Text;
using Broker.Services;
using BrokerCore.Models;
using FunctionPool.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Broker.Endpoints;

/// <summary>
/// Prometheus exposition format /metrics endpoint（#2 observability）。
///
/// 給 Prometheus / Grafana / Uptime Kuma 等外部監控系統 scrape。
/// 純文字回應、UTF-8、Content-Type: text/plain; version=0.0.4。
///
/// 暴露的 metrics（前綴 `b4a_`）：
///   - b4a_uptime_seconds                broker 跑了多久
///   - b4a_workers_connected{worker=}    各 worker 是否連上（0/1）
///   - b4a_auto_trader_enabled            auto-trader 開關（0/1）
///   - b4a_auto_trader_watch_count        監控清單長度
///   - b4a_auto_trader_position_count     active protection state 數量
///   - b4a_auto_trader_seconds_since_cycle 距上次 cycle 開始的秒數（heartbeat）
///   - b4a_circuit_breaker_dd_pct{exchange=}    當日 DD 百分比
///   - b4a_circuit_breaker_triggered{exchange=} 是否觸發（0/1）
///   - b4a_sl_flush_triggered             SL flush 凍結（0/1）
///   - b4a_sl_flush_recent_count          滑動視窗內近期 SL hit 次數
///   - b4a_alert_rules{enabled=}         告警規則總數（按 enabled 分群）
///   - b4a_alert_events_unacknowledged    未 ack 事件數
///
/// 設計選擇：
///   - 全部 gauge、不暴露 cumulative counter（沒持久化計數器、broker 重啟會歸零、
///     對 Prometheus 不友善）。需要 counter 時要用 BrokerDb 持久層才合理。
///   - 不依賴 prometheus-net package——手寫文字格式更輕量、無 transitive deps。
/// </summary>
public static class MetricsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 注意：這個 endpoint 不在 /api/v1 之下，根層直接掛 /metrics 是 Prometheus 慣例
        app.MapGet("/metrics", (HttpContext ctx) =>
        {
            var sb = new StringBuilder(2048);
            var sp = ctx.RequestServices;

            // ── broker uptime ─────────────────────────────────────
            var uptime = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
            EmitGauge(sb, "b4a_uptime_seconds", "Broker process uptime in seconds", uptime);

            // ── workers connected ─────────────────────────────────
            var registry = sp.GetService<IWorkerRegistry>();
            if (registry != null)
            {
                sb.AppendLine("# HELP b4a_workers_connected Worker connection state (0=disconnected, 1=connected)");
                sb.AppendLine("# TYPE b4a_workers_connected gauge");
                foreach (var (worker, capability) in new[]
                {
                    ("quote",    "quote.prices"),
                    ("strategy", "strategy.signal"),
                    ("risk",     "risk.check"),
                    ("trading",  "trading.account"),
                })
                {
                    var connected = registry.HasAvailableWorker(capability) ? 1 : 0;
                    sb.Append("b4a_workers_connected{worker=\"").Append(worker).Append("\"} ").Append(connected).AppendLine();
                }
                sb.AppendLine();
            }

            // ── AutoTrader state ──────────────────────────────────
            var autoTrader = sp.GetService<AutoTraderService>();
            if (autoTrader != null)
            {
                EmitGauge(sb, "b4a_auto_trader_enabled", "Auto-trader main loop enabled (0/1)",
                    autoTrader.IsEnabled ? 1d : 0d);
                EmitGauge(sb, "b4a_auto_trader_watch_count", "Number of symbols on auto-trader watch list",
                    (double)autoTrader.WatchList.Count);
                EmitGauge(sb, "b4a_auto_trader_position_count", "Number of positions with active protection state",
                    (double)autoTrader.PositionStates.Count);

                // Heartbeat：距上次 cycle 開始的秒數。null = 從未跑過、報 -1 讓 Grafana 顯示異常
                var sinceCycle = autoTrader.LastCycleAt is { } at
                    ? (DateTime.UtcNow - at).TotalSeconds
                    : -1d;
                EmitGauge(sb, "b4a_auto_trader_seconds_since_cycle",
                    "Seconds since last auto-trader cycle started (-1 = never ran)", sinceCycle);

                // Circuit breaker per exchange
                sb.AppendLine("# HELP b4a_circuit_breaker_dd_pct Current intraday drawdown % per exchange");
                sb.AppendLine("# TYPE b4a_circuit_breaker_dd_pct gauge");
                sb.AppendLine("# HELP b4a_circuit_breaker_triggered 1 if circuit breaker is currently active");
                sb.AppendLine("# TYPE b4a_circuit_breaker_triggered gauge");
                foreach (var (exchange, snapshot) in autoTrader.CircuitBreakerSnapshot)
                {
                    var s = snapshot;  // anonymous object; use reflection or known props
                    var props = s.GetType().GetProperties();
                    decimal dd = 0; bool triggered = false;
                    foreach (var p in props)
                    {
                        if (p.Name == "dd_pct") dd = (decimal)(p.GetValue(s) ?? 0m);
                        else if (p.Name == "triggered") triggered = (bool)(p.GetValue(s) ?? false);
                    }
                    sb.Append("b4a_circuit_breaker_dd_pct{exchange=\"").Append(EscapeLabel(exchange)).Append("\"} ").Append(dd).AppendLine();
                    sb.Append("b4a_circuit_breaker_triggered{exchange=\"").Append(EscapeLabel(exchange)).Append("\"} ").Append(triggered ? 1 : 0).AppendLine();
                }
                sb.AppendLine();

                EmitGauge(sb, "b4a_sl_flush_triggered", "SL flush freeze active state (0/1)",
                    autoTrader.SlFlushTriggered ? 1d : 0d);
                EmitGauge(sb, "b4a_sl_flush_recent_count", "SL hits within sliding window",
                    (double)autoTrader.RecentSlHits.Count);
            }

            // ── Alert rules / events ──────────────────────────────
            var alertRules = sp.GetService<AlertRulesService>();
            if (alertRules != null)
            {
                var enabled = alertRules.Rules.Values.Count(r => r.Enabled);
                var disabled = alertRules.Rules.Count - enabled;
                sb.AppendLine("# HELP b4a_alert_rules Number of alert rules by enabled state");
                sb.AppendLine("# TYPE b4a_alert_rules gauge");
                sb.Append("b4a_alert_rules{enabled=\"true\"} ").Append(enabled).AppendLine();
                sb.Append("b4a_alert_rules{enabled=\"false\"} ").Append(disabled).AppendLine();
                sb.AppendLine();

                var unack = alertRules.GetEvents(limit: 500, unacknowledgedOnly: true).Count;
                EmitGauge(sb, "b4a_alert_events_unacknowledged",
                    "Alert events still pending user acknowledgement", (double)unack);
            }

            return Results.Text(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        });
    }

    private static void EmitGauge(StringBuilder sb, string name, string help, double value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        sb.Append("# TYPE ").Append(name).AppendLine(" gauge");
        sb.Append(name).Append(' ').Append(value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.AppendLine();
    }

    private static void EmitGauge(StringBuilder sb, string name, string help, decimal value)
        => EmitGauge(sb, name, help, (double)value);

    private static string EscapeLabel(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
