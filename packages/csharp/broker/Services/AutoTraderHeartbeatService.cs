using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// AutoTrader 心跳看門狗 —— 真錢 bot 全靠單一 broker 的 sweep loop 跑軟 SL / 進出場;
/// 若 sweep 卡住(broker 半死、deadlock、worker 全斷)但 process 沒崩,沒人會發現、
/// 真錢倉位等於無人看管(軟 SL 不會觸發)。這個 watchdog 每 ~2 分檢查
/// AutoTraderService.LastCycleAt,超過 stale 門檻就推 Discord + LINE 告警、恢復後再推一則。
///
/// 只在 auto-trader enabled 時告警(故意停用時不吵)。env:
///   - AUTOTRADER_HEARTBEAT_ENABLED=false  關閉(預設開)
///   - AUTOTRADER_HEARTBEAT_STALE_SEC=N    自訂 stale 門檻;預設 = interval×3、最少 600s
/// </summary>
public class AutoTraderHeartbeatService : BackgroundService
{
    private readonly AutoTraderService _autoTrader;
    private readonly DiscordNotificationService _discord;
    private readonly LineNotificationService _line;
    private readonly ILogger<AutoTraderHeartbeatService> _logger;
    private readonly bool _enabled;
    private readonly int _staleSecOverride;
    private bool _alerted;

    public AutoTraderHeartbeatService(
        AutoTraderService autoTrader,
        DiscordNotificationService discord,
        LineNotificationService line,
        ILogger<AutoTraderHeartbeatService> logger)
    {
        _autoTrader = autoTrader;
        _discord = discord;
        _line = line;
        _logger = logger;
        _enabled = !string.Equals(
            Environment.GetEnvironmentVariable("AUTOTRADER_HEARTBEAT_ENABLED"), "false",
            StringComparison.OrdinalIgnoreCase);
        _staleSecOverride = int.TryParse(
            Environment.GetEnvironmentVariable("AUTOTRADER_HEARTBEAT_STALE_SEC"), out var s) && s > 0 ? s : 0;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_enabled)
        {
            _logger.LogInformation("AutoTraderHeartbeat disabled (AUTOTRADER_HEARTBEAT_ENABLED=false)");
            return;
        }
        _logger.LogInformation("AutoTraderHeartbeat watchdog started");

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(120), ct); }
            catch (OperationCanceledException) { break; }

            try { await CheckOnceAsync(ct); }
            catch (Exception ex) { _logger.LogError(ex, "AutoTraderHeartbeat: check failed"); }
        }
    }

    internal async Task CheckOnceAsync(CancellationToken ct)
    {
        // 停用中不告警(故意關的);恢復 enabled 後重新監看
        if (!_autoTrader.IsEnabled) { _alerted = false; return; }

        var last = _autoTrader.LastCycleAt;
        if (last == null) return;   // 剛啟動、還沒跑第一輪 sweep

        var staleSec = _staleSecOverride > 0 ? _staleSecOverride : Math.Max(_autoTrader.IntervalSeconds * 3, 600);
        var elapsed = (DateTime.UtcNow - last.Value).TotalSeconds;

        if (elapsed > staleSec && !_alerted)
        {
            _alerted = true;
            var mins = (int)(elapsed / 60);
            var title = "⚠️ AutoTrader 心跳停止";
            var body = $"sweep 已 **{mins} 分鐘**沒跑(上次 {last.Value:yyyy-MM-dd HH:mm:ss}Z、門檻 {staleSec}s)。\n" +
                       "真錢倉位可能無人看管(broker 軟 SL 不會觸發、只剩交易所端 bracket SL 兜底)。請檢查 broker / worker 連線。";
            _logger.LogWarning("AutoTraderHeartbeat STALE: {Mins}min since last cycle (threshold {Sec}s)", mins, staleSec);
            await _discord.SendAdHocAsync(title, body, 0xF6465D, ct);
            await _line.SendAdHocAsync(title, body, level: "warning", ct);
        }
        else if (elapsed <= staleSec && _alerted)
        {
            _alerted = false;
            var title = "✅ AutoTrader 心跳恢復";
            var body = $"sweep 恢復正常(上次 cycle {last.Value:yyyy-MM-dd HH:mm:ss}Z)。";
            _logger.LogInformation("AutoTraderHeartbeat recovered");
            await _discord.SendAdHocAsync(title, body, 0x0ECB81, ct);
            await _line.SendAdHocAsync(title, body, level: "success", ct);
        }
    }
}
