using System.Text;
using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 台股資金流每日彙整(2026-06-04)。抓 TWSE 三大法人買賣超(T86)+ 融資融券(MI_MARGN)、
/// 存進 tw_fund_flow_daily(「做資料」、累積歷史供日後因子驗證)、組 digest 推 Discord DM(「作推播」)。
///
/// digest 三段:① 全市場 外資買超/賣超 top ② 全市場 投信買超/賣超 top ③ watchlist 14 檔法人+融資融券。
/// 推播走 DiscordNotificationService(已 DM 模式 → 只有 operator 收得到、不進共享頻道)。
///
/// Schedule:
///   - TW_FUNDFLOW_AT_UTC_HOUR=9 預設(=17:00 TST、台股 T86/MI_MARGN 約 15-16:00 發布後);-1 完全關閉自動推。
///   - 每日 fire 只抓「當日 TST」(maxLookback=1)、非交易日無資料則靜默 skip(不洗版 / 不重推舊資料)。
///   - 手動端點 POST /push-tw-fundflow 用 maxLookback=7 找最近一個交易日(?dry=true 不推、只回 summary)。
///
/// 非真錢:純讀取公開資料 + 通知,無下單路徑。
/// </summary>
public class TwFundFlowService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly BrokerCore.Data.BrokerDb _db;
    private readonly DiscordNotificationService _discord;
    private readonly ILogger<TwFundFlowService> _logger;
    private readonly int _reportHourUtc;
    private readonly string[] _watchlist;

    private const int TopN = 6;          // 全市場買超/賣超榜各取幾名
    private const long LotShares = 1000; // 1 張 = 1000 股(三大法人 T86 單位股 → 換算張)

    // 預設 watchlist = scanner-seed 的 14 檔台股(去 .TW);TW_FUNDFLOW_WATCHLIST 可逗號覆寫。
    private static readonly string[] DefaultWatchlist =
        { "2330", "2317", "2454", "2308", "2303", "2881", "2882", "2891", "2412", "1301", "1303", "2002", "1216", "2912" };

    public TwFundFlowService(
        IHttpClientFactory httpFactory,
        BrokerCore.Data.BrokerDb db,
        DiscordNotificationService discord,
        ILogger<TwFundFlowService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _discord = discord;
        _logger = logger;
        _reportHourUtc = ParseIntEnv("TW_FUNDFLOW_AT_UTC_HOUR", defaultValue: 9, min: -1, max: 23);
        var raw = Environment.GetEnvironmentVariable("TW_FUNDFLOW_WATCHLIST");
        _watchlist = string.IsNullOrWhiteSpace(raw)
            ? DefaultWatchlist
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_reportHourUtc < 0)
        {
            _logger.LogInformation("TwFundFlowService disabled (TW_FUNDFLOW_AT_UTC_HOUR=-1)");
            return;
        }
        _logger.LogInformation("TwFundFlowService scheduled at UTC {Hour}:00 daily (TST {TstHour}:00), watchlist={N}",
            _reportHourUtc, (_reportHourUtc + 8) % 24, _watchlist.Length);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, now.Day, _reportHourUtc, 0, 0, DateTimeKind.Utc);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            try { await Task.Delay(nextRun - now, ct); }
            catch (OperationCanceledException) { break; }

            try { await BuildAndPushAsync(push: true, maxLookbackDays: 1, ct); }   // 排程只抓當日、休市靜默 skip
            catch (Exception ex) { _logger.LogError(ex, "TwFundFlow: scheduled build/push failed"); }
        }
    }

    /// <summary>
    /// 抓最近一個有資料的台股交易日(從今日 TST 往回最多 maxLookbackDays 天)、存 DB、組 digest、推 DM。
    /// push=false = dry-run(只回 summary、不推)。回 (ok, summary, hadData)。
    /// </summary>
    public async Task<(bool Ok, string Summary, bool HadData)> BuildAndPushAsync(
        bool push, int maxLookbackDays, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("twse-fundflow");
        http.Timeout = TimeSpan.FromSeconds(30);
        if (!http.DefaultRequestHeaders.Contains("User-Agent"))
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; b4a-fundflow/1.0)");

        var todayTst = DateTime.UtcNow.AddHours(8).Date;
        List<TwFundFlowDaily>? rows = null;
        string isoDate = "";
        for (int back = 0; back < Math.Max(1, maxLookbackDays); back++)
        {
            var day = todayTst.AddDays(-back);
            var (hasData, fetched) = await TwseFundFlowClient.FetchDayAsync(http, day, ct);
            if (hasData) { rows = fetched; isoDate = day.ToString("yyyy-MM-dd"); break; }
        }

        if (rows == null || rows.Count == 0)
        {
            _logger.LogInformation("TwFundFlow: no data within {N}d lookback (台股休市 / 資料未發布)", maxLookbackDays);
            return (true, $"近 {maxLookbackDays} 日無台股資料(休市或未發布)", false);
        }

        StoreRows(isoDate, rows);

        var body = BuildDigest(isoDate, rows);
        if (!push)
        {
            _logger.LogInformation("TwFundFlow dry-run (no push): date={Date} stocks={N}", isoDate, rows.Count);
            return (true, body, true);
        }
        var (ok, err) = await _discord.SendAdHocAsync($"🇹🇼 台股資金流 · {isoDate}", body, 0x2B6CB0, ct);
        _logger.LogInformation("TwFundFlow pushed: discord={Ok} date={Date} stocks={N} err={Err}",
            ok, isoDate, rows.Count, err ?? "-");
        return (ok, body, true);
    }

    /// <summary>冪等存檔:同交易日先 DELETE 再 Insert 全量(重抓覆寫)。整批包在 transaction。</summary>
    private void StoreRows(string isoDate, List<TwFundFlowDaily> rows)
    {
        try
        {
            _db.InTransaction(() =>
            {
                _db.Execute("DELETE FROM tw_fund_flow_daily WHERE trade_date = @d", new { d = isoDate });
                foreach (var r in rows) _db.Insert(r);
            });
            _logger.LogDebug("TwFundFlow: stored {N} rows for {Date}", rows.Count, isoDate);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: store failed for {Date}", isoDate); }
    }

    /// <summary>組 digest:全市場外資/投信買超賣超 top + watchlist 14 檔。三大法人換算張(/1000)。</summary>
    private string BuildDigest(string isoDate, List<TwFundFlowDaily> rows)
    {
        // 全市場榜只取「普通個股」(代號非 0 開頭 → 排除 0050/0056 等 ETF 的巨量股數蓋過個股)
        var stocks = rows.Where(r => r.StockCode.Length == 4 && r.StockCode[0] != '0').ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"**台股三大法人 + 融資融券 · {isoDate}**");
        sb.AppendLine($"_全市場 {rows.Count} 檔(含 ETF);榜單為普通個股、單位張(正=買超/增、負=賣超/減)_");

        AppendRank(sb, "🟢 外資買超 top", stocks.OrderByDescending(r => r.ForeignNet), r => r.ForeignNet);
        AppendRank(sb, "🔴 外資賣超 top", stocks.OrderBy(r => r.ForeignNet), r => r.ForeignNet);
        AppendRank(sb, "🟢 投信買超 top", stocks.OrderByDescending(r => r.TrustNet), r => r.TrustNet);
        AppendRank(sb, "🔴 投信賣超 top", stocks.OrderBy(r => r.TrustNet), r => r.TrustNet);

        // watchlist 段:逐檔法人合計 + 外投自分項 + 融資/融券變化
        var byCode = rows.ToDictionary(r => r.StockCode, r => r);
        sb.AppendLine();
        sb.AppendLine($"**📌 我的 watchlist({_watchlist.Length} 檔)**");
        foreach (var code in _watchlist)
        {
            if (!byCode.TryGetValue(code, out var r)) { sb.AppendLine($"・{code}: (無資料)"); continue; }
            long marginChg = r.MarginBalance - r.MarginPrev;
            long shortChg = r.ShortBalance - r.ShortPrev;
            sb.AppendLine(
                $"・{r.StockName}({code}): 法人{Sgn(Lot(r.TotalNet))} " +
                $"(外{Sgn(Lot(r.ForeignNet))} 投{Sgn(Lot(r.TrustNet))} 自{Sgn(Lot(r.DealerNet))}) " +
                $"· 融資{Sgn(marginChg)} 融券{Sgn(shortChg)}");
        }
        return sb.ToString();
    }

    private void AppendRank(StringBuilder sb, string header, IEnumerable<TwFundFlowDaily> ordered, Func<TwFundFlowDaily, long> metric)
    {
        sb.AppendLine();
        sb.AppendLine($"**{header}**");
        foreach (var r in ordered.Take(TopN))
            sb.AppendLine($"・{r.StockName}({r.StockCode}) {Sgn(Lot(metric(r)))} 張");
    }

    private static long Lot(long shares) => shares / LotShares;   // 股 → 張
    private static string Sgn(long v) => (v >= 0 ? "+" : "") + v.ToString("N0");

    private static int ParseIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
}
