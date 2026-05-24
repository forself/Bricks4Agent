using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// ETH/BTC 比值告警 —— 使用者的「ETH 補漲論」客觀觸發點。
/// ETH 長年跑輸 BTC、比值壓縮;若比值持續往上 = ETH 相對轉強、補漲論成形;掉回去 = 又是價值陷阱。
/// 每 30 分讀 BingX markPrice 算 ETH/BTC,跨「上緣 / 下緣」band 才推(0.025–0.030 區間當 hysteresis 防洗版)。
///
/// env(都有預設、不設也能跑;要在 compose 接線才吃得到 env、見 reference_vps_deploy):
///   - ETHBTC_RATIO_ALERT_ENABLED=false 關閉(預設開)
///   - ETHBTC_RATIO_UPPER(預設 0.030)、ETHBTC_RATIO_LOWER(預設 0.025)
/// </summary>
public class EthBtcRatioAlertService : BackgroundService
{
    private readonly DiscordNotificationService _discord;
    private readonly LineNotificationService _line;
    private readonly ILogger<EthBtcRatioAlertService> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly bool _enabled;
    private readonly decimal _upper;
    private readonly decimal _lower;
    private string _lastZone = "";   // "above" / "mid" / "below";空 = 還沒建基準

    public EthBtcRatioAlertService(
        DiscordNotificationService discord, LineNotificationService line,
        ILogger<EthBtcRatioAlertService> logger)
    {
        _discord = discord;
        _line = line;
        _logger = logger;
        _enabled = !string.Equals(Environment.GetEnvironmentVariable("ETHBTC_RATIO_ALERT_ENABLED"), "false",
            StringComparison.OrdinalIgnoreCase);
        _upper = ParseDec("ETHBTC_RATIO_UPPER", 0.030m);
        _lower = ParseDec("ETHBTC_RATIO_LOWER", 0.025m);
    }

    private static decimal ParseDec(string env, decimal def) =>
        decimal.TryParse(Environment.GetEnvironmentVariable(env), out var v) && v > 0 ? v : def;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_enabled) { _logger.LogInformation("EthBtcRatioAlert disabled"); return; }
        _logger.LogInformation("EthBtcRatioAlert started (upper={Up} lower={Lo})", _upper, _lower);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(30), ct); }
            catch (OperationCanceledException) { break; }
            try { await CheckOnceAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "EthBtcRatioAlert: check failed"); }
        }
    }

    internal async Task CheckOnceAsync(CancellationToken ct)
    {
        var eth = await MarkPriceAsync("ETH-USDT", ct);
        var btc = await MarkPriceAsync("BTC-USDT", ct);
        if (eth <= 0 || btc <= 0) return;
        var ratio = eth / btc;

        var zone = ratio > _upper ? "above" : ratio < _lower ? "below" : "mid";
        if (_lastZone == "") { _lastZone = zone; return; }   // 首次只建基準、不推
        if (zone == _lastZone) return;
        var prev = _lastZone;
        _lastZone = zone;

        // 只在「進入 above(轉強)」或「進入 below(轉弱)」時推;回到 mid 不吵
        if (zone == "above")
        {
            await PushAsync("🚀 ETH/BTC 突破上緣 — ETH 相對轉強",
                $"ETH/BTC = **{ratio:F5}** 突破 {_upper:F3}(ETH {eth:F0} / BTC {btc:F0})。\nETH 長年跑輸 BTC 的趨勢轉向、**補漲論成形**。",
                0x0ECB81, "success", ct);
        }
        else if (zone == "below")
        {
            await PushAsync("📉 ETH/BTC 跌破下緣 — 回到價值陷阱",
                $"ETH/BTC = **{ratio:F5}** 跌破 {_lower:F3}(ETH {eth:F0} / BTC {btc:F0})。\nETH 相對再轉弱、**補漲論轉冷**。",
                0xF6465D, "warning", ct);
        }
        _logger.LogInformation("EthBtcRatioAlert: zone {Prev}→{Zone} ratio={Ratio:F5}", prev, zone, ratio);
    }

    private async Task PushAsync(string title, string body, int color, string level, CancellationToken ct)
    {
        try { await _discord.SendAdHocAsync(title, body, color, ct); } catch { }
        try { await _line.SendAdHocAsync(title, body, level: level, ct); } catch { }
    }

    // BingX premiumIndex 是 public endpoint、不用簽
    private async Task<decimal> MarkPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"https://open-api.bingx.com/openApi/swap/v2/quote/premiumIndex?symbol={symbol}", ct);
            var doc = JsonDocument.Parse(json).RootElement;
            if (!doc.TryGetProperty("data", out var d)) return 0m;
            var mp = d.TryGetProperty("markPrice", out var m) ? m.GetString() : null;
            return decimal.TryParse(mp, out var v) ? v : 0m;
        }
        catch { return 0m; }
    }
}
