using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 橫斷面動量 shadow runner —— portfolio-level / 跨幣排序策略,現有「一 watch = 一(策略,幣)」
/// 的 per-symbol 模型裝不下,所以獨立成一個 BackgroundService。
///
/// 為什麼 shadow:xsmom 真正的 edge 是「多空市場中性」(純多版多是 crypto beta),要放空;
/// 但我們沒有 crypto perp 的 paper 帳戶(binance paper 是現貨不能空、bingx 是真錢)。
/// → 用真實行情即時算籃子 + 追蹤「假想」P&L、**完全不下單**,零風險 forward 驗證。
/// 研究見 tools/strat-validate --xsmom(短週期 lb30/rebal7 OOS Sharpe 0.34、與 decorr4 ρ0.26)。
///
/// 每 rebal 期:抓宇宙日線 → 過去 lookback 報酬排序 → long topK 強 / short topK 弱(等權)→
/// 用上期進場價算這期報酬、累進假想權益 → 換新籃子。狀態存 /data/xsmom-shadow.json(重啟續跑)。
///
/// env(要在 compose 接線才進容器):
///   XSMOM_SHADOW_ENABLED=true 開(預設關)、_UNIVERSE(逗號幣清單)、_LOOKBACK_D(30)、
///   _REBAL_D(7)、_TOPK(3)、_COST(0.0008/邊)、_NOTIFY=false 關 Discord 推播
/// </summary>
public class XsMomShadowService : BackgroundService
{
    private readonly DiscordNotificationService _discord;
    private readonly ILogger<XsMomShadowService> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly bool _enabled;
    private readonly bool _notify;
    private readonly string[] _universe;
    private readonly int _lookbackD;
    private readonly int _rebalD;
    private readonly int _topK;
    private readonly decimal _cost;
    private const string StatePath = "/data/xsmom-shadow.json";

    public XsMomShadowService(DiscordNotificationService discord, ILogger<XsMomShadowService> logger)
    {
        _discord = discord;
        _logger = logger;
        _enabled = string.Equals(Environment.GetEnvironmentVariable("XSMOM_SHADOW_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
        _notify = !string.Equals(Environment.GetEnvironmentVariable("XSMOM_SHADOW_NOTIFY"), "false", StringComparison.OrdinalIgnoreCase);
        var uni = Environment.GetEnvironmentVariable("XSMOM_SHADOW_UNIVERSE");
        _universe = !string.IsNullOrWhiteSpace(uni)
            ? uni.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "BTCUSDT","ETHUSDT","SOLUSDT","BNBUSDT","XRPUSDT","ADAUSDT","DOGEUSDT","AVAXUSDT",
                      "LINKUSDT","LTCUSDT","DOTUSDT","ATOMUSDT","TRXUSDT","UNIUSDT","NEARUSDT","APTUSDT",
                      "ARBUSDT","OPUSDT","SUIUSDT","INJUSDT" };
        _lookbackD = ParseInt("XSMOM_SHADOW_LOOKBACK_D", 30);
        _rebalD    = ParseInt("XSMOM_SHADOW_REBAL_D", 7);
        _topK      = ParseInt("XSMOM_SHADOW_TOPK", 3);
        _cost      = decimal.TryParse(Environment.GetEnvironmentVariable("XSMOM_SHADOW_COST"), out var c) && c >= 0 ? c : 0.0008m;
    }

    private static int ParseInt(string env, int def) =>
        int.TryParse(Environment.GetEnvironmentVariable(env), out var v) && v > 0 ? v : def;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_enabled) { _logger.LogInformation("XsMomShadow disabled (set XSMOM_SHADOW_ENABLED=true)"); return; }
        _logger.LogInformation("XsMomShadow started: universe={N} lookback={Lb}d rebal={Rb}d topK={K} (SHADOW、不下單)",
            _universe.Length, _lookbackD, _rebalD, _topK);
        // 啟動等 20s 讓其他服務先起來
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "XsMomShadow tick failed"); }
            try { await Task.Delay(TimeSpan.FromHours(6), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var st = LoadState();
        // rebal 到期才動(首次無狀態也視為到期、建初始籃子)
        if (st != null && (DateTime.UtcNow - st.LastRebalUtc).TotalDays < _rebalD) return;

        // 抓宇宙日線收盤(lookback+3 根)
        var closes = new Dictionary<string, List<decimal>>();
        foreach (var sym in _universe)
        {
            var cl = await FetchClosesAsync(sym, _lookbackD + 3, ct);
            if (cl.Count >= _lookbackD + 1) closes[sym] = cl;
        }
        if (closes.Count < _topK * 2 + 1) { _logger.LogWarning("XsMomShadow: 宇宙資料不足 ({N})", closes.Count); return; }

        decimal periodRet = 0m;
        // 既有籃子 → 用上期進場價算這期報酬
        if (st != null && (st.Long.Count > 0 || st.Short.Count > 0))
        {
            decimal LegRet(List<ShadowPos> set) => set.Count == 0 ? 0m
                : set.Where(p => closes.ContainsKey(p.Coin) && p.Entry > 0m)
                     .Select(p => closes[p.Coin][^1] / p.Entry - 1m)
                     .DefaultIfEmpty(0m).Average();
            periodRet = LegRet(st.Long) - LegRet(st.Short);
        }

        // 重排:過去 lookback 報酬
        var ranked = closes.Select(kv => (coin: kv.Key, r: kv.Value[^1] / kv.Value[^(_lookbackD + 1)] - 1m))
                           .OrderByDescending(x => x.r).ToList();
        var newLong  = ranked.Take(_topK).Select(x => new ShadowPos { Coin = x.coin, Entry = closes[x.coin][^1] }).ToList();
        var newShort = ranked.AsEnumerable().Reverse().Take(_topK).Select(x => new ShadowPos { Coin = x.coin, Entry = closes[x.coin][^1] }).ToList();

        // 換手成本(改變比例 × 來回)
        decimal turnoverCost = 0m;
        if (st != null)
        {
            var oldL = st.Long.Select(p => p.Coin).ToHashSet();
            var oldS = st.Short.Select(p => p.Coin).ToHashSet();
            int changed = newLong.Count(p => !oldL.Contains(p.Coin)) + oldL.Count(c => !newLong.Any(p => p.Coin == c))
                        + newShort.Count(p => !oldS.Contains(p.Coin)) + oldS.Count(c => !newShort.Any(p => p.Coin == c));
            int basket = _topK * 2;
            turnoverCost = basket > 0 ? (decimal)changed / (2 * basket) * 2m * _cost : 0m;
        }

        var prevEq = st?.Equity ?? 1m;
        var newEq = st == null ? 1m : prevEq * (1m + periodRet - turnoverCost);

        var next = new ShadowState
        {
            StartedUtc   = st?.StartedUtc ?? DateTime.UtcNow,
            LastRebalUtc = DateTime.UtcNow,
            Periods      = (st?.Periods ?? 0) + 1,
            Equity       = newEq,
            LastPeriodRet = st == null ? 0m : periodRet - turnoverCost,
            Long  = newLong,
            Short = newShort,
        };
        SaveState(next);

        var since = (DateTime.UtcNow - next.StartedUtc).TotalDays;
        _logger.LogInformation("XsMomShadow rebal #{P}: periodRet {R:P2} equity {E:F4} (起算{D:F0}天) · long [{L}] short [{S}]",
            next.Periods, next.LastPeriodRet, next.Equity, since,
            string.Join(",", newLong.Select(p => p.Coin.Replace("USDT", ""))),
            string.Join(",", newShort.Select(p => p.Coin.Replace("USDT", ""))));

        // 首次只建基準、不推;之後每次 rebal 推一則(週頻、可關)
        if (_notify && st != null)
        {
            var pnlPct = (next.Equity - 1m) * 100m;
            await PushAsync(
                $"🧪 xsmom shadow rebal #{next.Periods}(假想、未下單)",
                $"本期報酬 **{next.LastPeriodRet:P2}** · 起算累計 **{pnlPct:+0.0;-0.0}%**(equity {next.Equity:F3}、{since:F0} 天)\n" +
                $"📈 long: {string.Join(" ", newLong.Select(p => p.Coin.Replace("USDT", "")))}\n" +
                $"📉 short: {string.Join(" ", newShort.Select(p => p.Coin.Replace("USDT", "")))}",
                pnlPct >= 0 ? 0x0ECB81 : 0xF6465D, ct);
        }
    }

    private async Task<List<decimal>> FetchClosesAsync(string sym, int limit, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"https://api.binance.com/api/v3/klines?symbol={sym}&interval=1d&limit={limit}", ct);
            using var doc = JsonDocument.Parse(json);
            var outl = new List<decimal>();
            foreach (var k in doc.RootElement.EnumerateArray())
                if (decimal.TryParse(k[4].GetString(), System.Globalization.CultureInfo.InvariantCulture, out var c)) outl.Add(c);
            return outl;
        }
        catch { return new List<decimal>(); }
    }

    private async Task PushAsync(string title, string body, int color, CancellationToken ct)
    {
        try { await _discord.SendAdHocAsync(title, body, color, ct); } catch { }
    }

    private ShadowState? LoadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<ShadowState>(File.ReadAllText(StatePath)) : null; }
        catch { return null; }
    }

    private void SaveState(ShadowState s)
    {
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(s)); }
        catch (Exception ex) { _logger.LogWarning(ex, "XsMomShadow: save state failed"); }
    }

    public class ShadowState
    {
        public decimal Equity { get; set; } = 1m;
        public DateTime LastRebalUtc { get; set; }
        public DateTime StartedUtc { get; set; }
        public int Periods { get; set; }
        public decimal LastPeriodRet { get; set; }
        public List<ShadowPos> Long { get; set; } = new();
        public List<ShadowPos> Short { get; set; } = new();
    }
    public class ShadowPos { public string Coin { get; set; } = ""; public decimal Entry { get; set; } }
}
