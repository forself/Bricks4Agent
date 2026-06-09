using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 台股資金流每日報表(2026-06-04;個人用、與 B4A 量化/專案本體脫鉤)。
/// 抓 TWSE 三大法人(T86)+ 融資融券(MI_MARGN)+ 收盤(STOCK_DAY_ALL)、存 tw_fund_flow_daily、
/// 算「完整日報」(重點摘要 + 外資連續買賣超 + 三大法人/外資/投信 金額榜 + 融資融券 + watchlist),
/// 雙輸出:① Discord DM 精簡摘要 + 報表連結 ② 完整 HTML 報表寫進 wwwroot(掛 dashboard 網域)。
///
/// Schedule:TW_FUNDFLOW_AT_UTC_HOUR=9 預設(=17:00 TST、台股資料發布後);-1 關。
/// 啟動時:歷史不足會 backfill 最近交易日(讓「連 N 日」即時可用)+ 生成一次 HTML(不推 DM)。
/// 純讀取公開資料 + 通知,無下單路徑(非真錢)。
/// </summary>
public class TwFundFlowService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly BrokerCore.Data.BrokerDb _db;
    private readonly DiscordNotificationService _discord;
    private readonly LineNotificationService _line;
    private readonly ILogger<TwFundFlowService> _logger;
    private readonly int _reportHourUtc;
    private readonly string[] _watchlist;
    private readonly string _htmlPath;        // 完整版(含 watchlist)→ dashboard(Access 限本人)
    private readonly string _familyHtmlPath;  // family 版(去 watchlist)→ 公開(給家人連結)
    private readonly string _reportUrl;
    private readonly string[] _lineTo;      // 額外推 LINE 的 userId(如家人);空=不推 LINE
    private readonly string _publicUrl;      // LINE 給家人的「公開報表」連結(免 Cloudflare Access)
    private readonly bool _includeOtc;       // 是否把上櫃(TPEx)併進「各產業資金流」(預設 true)

    private const int HistoryWindow = 8;     // 連續買賣超查的歷史天數窗
    private const int MinHistoryDates = 6;   // 啟動時少於這數量就 backfill
    private const int BackfillCalendarDays = 18;  // backfill 往回掃的日曆天(撈 ~12 交易日)

    private static readonly string[] DefaultWatchlist =
        { "2330", "2317", "2454", "2308", "2303", "2881", "2882", "2891", "2412", "1301", "1303", "2002", "1216", "2912" };

    public TwFundFlowService(
        IHttpClientFactory httpFactory,
        BrokerCore.Data.BrokerDb db,
        DiscordNotificationService discord,
        LineNotificationService line,
        ILogger<TwFundFlowService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _discord = discord;
        _line = line;
        _logger = logger;
        _reportHourUtc = ParseIntEnv("TW_FUNDFLOW_AT_UTC_HOUR", defaultValue: 9, min: -1, max: 23);
        var raw = Environment.GetEnvironmentVariable("TW_FUNDFLOW_WATCHLIST");
        _watchlist = string.IsNullOrWhiteSpace(raw)
            ? DefaultWatchlist
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _htmlPath = Environment.GetEnvironmentVariable("TW_FUNDFLOW_REPORT_HTML") ?? "/app/wwwroot/tw-fundflow.html";
        _familyHtmlPath = Environment.GetEnvironmentVariable("TW_FUNDFLOW_FAMILY_HTML") ?? "/app/wwwroot/tw-fundflow-family.html";
        _reportUrl = Environment.GetEnvironmentVariable("TW_FUNDFLOW_REPORT_URL")
                     ?? "https://dashboard.b4a-trading.app/tw-fundflow.html";
        _lineTo = (Environment.GetEnvironmentVariable("TW_FUNDFLOW_LINE_TO") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // 給家人的公開連結(免 Access);沒設就退回 dashboard URL(家人會被 Access 擋、需設了才推)
        _publicUrl = Environment.GetEnvironmentVariable("TW_FUNDFLOW_PUBLIC_URL") ?? _reportUrl;
        _includeOtc = (Environment.GetEnvironmentVariable("TW_FUNDFLOW_INCLUDE_OTC") ?? "true").Trim().ToLowerInvariant() is "true" or "1";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_reportHourUtc < 0)
        {
            _logger.LogInformation("TwFundFlowService disabled (TW_FUNDFLOW_AT_UTC_HOUR=-1)");
            return;
        }
        _logger.LogInformation("TwFundFlowService scheduled at UTC {Hour}:00 (TST {Tst}:00), watchlist={N}, html={Html}",
            _reportHourUtc, (_reportHourUtc + 8) % 24, _watchlist.Length, _htmlPath);

        // 啟動:歷史不足先 backfill、再生成一次 HTML(不推 DM、避免重啟洗版)
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);   // 讓 DB/migration 先就緒
            await BackfillIfNeededAsync(ct);
            await BuildAndPushAsync(push: false, maxLookbackDays: 7, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: startup backfill/generate failed"); }

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, now.Day, _reportHourUtc, 0, 0, DateTimeKind.Utc);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            try { await Task.Delay(nextRun - now, ct); }
            catch (OperationCanceledException) { break; }

            try { await BuildAndPushAsync(push: true, maxLookbackDays: 1, ct); }
            catch (Exception ex) { _logger.LogError(ex, "TwFundFlow: scheduled build/push failed"); }
        }
    }

    /// <summary>
    /// 抓最近一個有資料的交易日 → 存 → 組完整報表 → 寫 HTML → (push) 推 DM 摘要+連結。
    /// push=false = 只生成 HTML/存檔、不推。回 (ok, discordSummary, hadData)。
    /// </summary>
    public async Task<(bool Ok, string Summary, bool HadData)> BuildAndPushAsync(
        bool push, int maxLookbackDays, CancellationToken ct, bool familyPreview = false)
    {
        var http = CreateHttp();

        var todayTst = DateTime.UtcNow.AddHours(8).Date;
        List<TwFundFlowDaily>? rows = null;
        string isoDate = "";
        var matchedDay = todayTst;
        for (int back = 0; back < Math.Max(1, maxLookbackDays); back++)
        {
            var day = todayTst.AddDays(-back);
            var (hasData, fetched) = await TwseFundFlowClient.FetchDayAsync(http, day, ct);
            if (hasData) { rows = fetched; isoDate = day.ToString("yyyy-MM-dd"); matchedDay = day; break; }
        }
        if (rows == null || rows.Count == 0)
        {
            _logger.LogInformation("TwFundFlow: no data within {N}d lookback (休市/未發布)", maxLookbackDays);
            return (true, $"近 {maxLookbackDays} 日無台股資料(休市或未發布)", false);
        }

        // 收盤價:用「報表日自己的」全市場收盤(MI_INDEX 指定日期)→ 永遠對得上、穩定顯示億元
        // (根治「張↔億元跳動」)。MI_INDEX 抓不到 → 退回 DB 既存的當日 close_price(之前跑存過)。
        var closes = await TwseFundFlowClient.FetchClosesForDateAsync(http, matchedDay, ct);
        if (closes.Count == 0) closes = LoadStoredCloses(isoDate);
        foreach (var r in rows)
            if (closes.TryGetValue(r.StockCode, out var c)) r.ClosePrice = c;

        StoreRows(isoDate, rows);

        // 組報表:收盤 dict(報表日自己的)+ 外資歷史序列(連續買賣超)
        var closesForReport = closes.Count > 0
            ? closes
            : rows.Where(r => r.ClosePrice > 0).ToDictionary(r => r.StockCode, r => r.ClosePrice);
        var foreignHist = LoadForeignHistory(isoDate, HistoryWindow);
        var sectorMap = await GetIndustryMapAsync(http, ct);   // 上市個股→產業名
        // 漲跌%:用 DB 前一交易日收盤算((今收-前收)/前收),不靠解析 MI_INDEX 漲跌符號 HTML
        var prevCloses = LoadPrevCloses(isoDate);
        var changePct = new Dictionary<string, decimal>();
        foreach (var kv in closesForReport)
            if (prevCloses.TryGetValue(kv.Key, out var pc) && pc > 0)
                changePct[kv.Key] = Math.Round((kv.Value - pc) / pc * 100m, 1);

        // 併入上櫃(TPEx)— 只在報表「當日」、in-memory(不存 DB)、失敗不致命 → 讓「各產業資金流」含上櫃。
        var reportRows = rows;
        var reportCloses = closesForReport;
        if (_includeOtc)
        {
            try
            {
                var tpex = await TpexFundFlowClient.FetchAsync(http, ct);
                if (tpex.Date == isoDate && tpex.Rows.Count > 0)
                {
                    var seen = rows.Select(r => r.StockCode).ToHashSet();
                    var otcRows = tpex.Rows.Where(r => seen.Add(r.StockCode)).ToList();   // 防跟上市撞代號
                    reportRows = new List<TwFundFlowDaily>(rows); reportRows.AddRange(otcRows);
                    reportCloses = new Dictionary<string, decimal>(closesForReport);
                    foreach (var kv in tpex.Closes) reportCloses.TryAdd(kv.Key, kv.Value);
                    var otcSectors = await GetTpexIndustryMapAsync(http, ct);
                    sectorMap = new Dictionary<string, string>(sectorMap);
                    foreach (var kv in otcSectors) sectorMap.TryAdd(kv.Key, kv.Value);
                    _logger.LogInformation("TwFundFlow: merged OTC +{N} stocks (sectors)", otcRows.Count);
                }
                else _logger.LogInformation("TwFundFlow: OTC skipped (date {D} != {Iso} or empty)", tpex.Date, isoDate);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: OTC merge failed (上市照常)"); }
        }

        // 大盤情緒:外資臺股期貨未平倉淨額(TAIFEX)— 失敗不致命(略過該段)
        TaifexClient.FuturesSentiment? sentiment = null;
        try { sentiment = await TaifexClient.FetchForeignTxOiAsync(http, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: TAIFEX sentiment fetch failed"); }

        var sectorHist = LoadSectorForeignHistory(isoDate, HistoryWindow, sectorMap, reportCloses);   // 產業連續同向(DB、現價加權、不含報表日)
        // 把報表日當天 live 的外資(現價加權)按產業加總、prepend 到序列最前 → consec 跟畫面外資「億」方向一致
        var todayBySector = new Dictionary<string, long>();
        foreach (var r in reportRows)
        {
            if (!(r.StockCode.Length == 4 && r.StockCode[0] != '0')) continue;
            if (!sectorMap.TryGetValue(r.StockCode, out var sec)) continue;
            if (!reportCloses.TryGetValue(r.StockCode, out var px) || px <= 0) continue;
            todayBySector.TryGetValue(sec, out var cur);
            todayBySector[sec] = cur + (long)(r.ForeignNet * px / 100000m);
        }
        foreach (var (sec, todayNet) in todayBySector)
        {
            if (!sectorHist.TryGetValue(sec, out var series)) { series = new List<long>(); sectorHist[sec] = series; }
            series.Insert(0, todayNet);   // 報表日在最前(LoadSectorForeignHistory 已排除報表日、不重複)
        }
        var report = TwFundFlowReport.Build(isoDate, reportRows, reportCloses, foreignHist, _watchlist, sectorMap, changePct, sentiment, sectorHist);

        // 寫兩份 HTML:完整(含 watchlist)→ dashboard;family(去 watchlist)→ 公開給家人(寫檔失敗不致命)
        WriteHtml(_htmlPath, TwFundFlowReport.RenderHtml(report, includeWatchlist: true));
        WriteHtml(_familyHtmlPath, TwFundFlowReport.RenderHtml(report, includeWatchlist: false));

        var summary = TwFundFlowReport.RenderDiscord(report, _reportUrl);
        if (!push)
        {
            _logger.LogInformation("TwFundFlow generated (no push): date={Date} stocks={N} useAmount={A}",
                isoDate, rows.Count, closes.Count > 0);
            return (true, summary, true);
        }

        // 家人版預覽:把「純產業 sectorFocus」版推到 operator 自己的 Discord(不碰 LINE/家人),給上線前看成品。
        if (familyPreview)
        {
            var preview = TwFundFlowReport.RenderDiscord(report, _publicUrl, includeWatchlist: false, sectorFocus: true);
            var (pok, perr) = await _discord.SendAdHocAsync($"🇹🇼 台股資金流日報(家人版預覽) · {isoDate}", preview, 0x9b59b6, ct);
            _logger.LogInformation("TwFundFlow family-preview → Discord: ok={Ok} err={Err}", pok, perr ?? "-");
            return (pok, preview, true);
        }

        var (ok, err) = await _discord.SendAdHocAsync($"🇹🇼 台股資金流日報 · {isoDate}", summary, 0x2B6CB0, ct);
        _logger.LogInformation("TwFundFlow pushed: discord={Ok} date={Date} stocks={N} err={Err}",
            ok, isoDate, rows.Count, err ?? "-");

        // 額外推 LINE 給家人(gated:設了 TW_FUNDFLOW_LINE_TO 才推)。用公開連結(免 Access)、純文字(LINE 不吃 markdown 去掉 **)。
        if (_lineTo.Length > 0 && _line.IsEnabledInConfig)
        {
            // 推給家人:以產業為主(sectorFocus)+ 省略 watchlist(個人關注清單不外送)
            var lineBody = TwFundFlowReport.RenderDiscord(report, _publicUrl, includeWatchlist: false, sectorFocus: true).Replace("**", "");
            foreach (var to in _lineTo)
            {
                var (lok, lerr) = await _line.SendAdHocToAsync(to, $"🇹🇼 台股資金流日報 · {isoDate}", lineBody, "info", ct);
                _logger.LogInformation("TwFundFlow LINE push to {To}: ok={Ok} err={Err}",
                    to[..Math.Min(6, to.Length)], lok, lerr ?? "-");
            }
        }
        return (ok, summary, true);
    }

    /// <summary>下載用:重建最新家人版報表(push:false=寫檔、不推通知)後回傳 family HTML 內容。
    /// 重建失敗則退回上次寫好的檔(graceful)。給「匯出 HTML 給家人本機開」用(繞過 LINE 內建瀏覽器)。</summary>
    public async Task<string?> GetFamilyHtmlAsync(CancellationToken ct)
    {
        try { await BuildAndPushAsync(push: false, maxLookbackDays: 7, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow download: 重建失敗、改回傳上次檔"); }
        return File.Exists(_familyHtmlPath) ? await File.ReadAllTextAsync(_familyHtmlPath, ct) : null;
    }

    /// <summary>歷史不足(distinct 日期 &lt; MinHistoryDates)→ backfill 最近 BackfillCalendarDays 內的交易日(含當日收盤)。</summary>
    private async Task BackfillIfNeededAsync(CancellationToken ct)
    {
        int have;
        try { have = _db.Query<TwFundFlowDaily>("SELECT DISTINCT trade_date FROM tw_fund_flow_daily").Count; }
        catch { have = 0; }
        if (have >= MinHistoryDates) return;

        _logger.LogInformation("TwFundFlow: backfilling history (have {Have} dates < {Min})", have, MinHistoryDates);
        var http = CreateHttp();
        var todayTst = DateTime.UtcNow.AddHours(8).Date;
        int filled = 0;
        for (int back = 0; back <= BackfillCalendarDays && !ct.IsCancellationRequested; back++)
        {
            var day = todayTst.AddDays(-back);
            string iso = day.ToString("yyyy-MM-dd");
            try
            {
                if (_db.Query<TwFundFlowDaily>("SELECT 1 FROM tw_fund_flow_daily WHERE trade_date=@d LIMIT 1", new { d = iso }).Count > 0)
                    continue;   // 已有就跳過
                var (hasData, rows) = await TwseFundFlowClient.FetchDayAsync(http, day, ct);
                if (!hasData) continue;   // 非交易日
                // 也補抓該日收盤(MI_INDEX 指定日)→ 歷史日金額也算得出、render 穩定億元
                var closes = await TwseFundFlowClient.FetchClosesForDateAsync(http, day, ct);
                foreach (var r in rows)
                    if (closes.TryGetValue(r.StockCode, out var c)) r.ClosePrice = c;
                StoreRows(iso, rows);
                filled++;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "TwFundFlow backfill {Day} skip", iso); }
        }
        _logger.LogInformation("TwFundFlow: backfill done, +{Filled} trading days", filled);
    }

    /// <summary>讀最近 k 個交易日的外資 net 序列(code → recent-first list),給連續買賣超用。</summary>
    private Dictionary<string, List<long>> LoadForeignHistory(string reportDate, int k)
    {
        var result = new Dictionary<string, List<long>>();
        try
        {
            var dates = _db.Query<TwFundFlowDaily>(
                "SELECT DISTINCT trade_date FROM tw_fund_flow_daily WHERE trade_date <= @d ORDER BY trade_date DESC LIMIT @k",
                new { d = reportDate, k })
                .Select(r => r.TradeDate).ToList();
            if (dates.Count == 0) return result;
            string minDate = dates.Min()!;

            var rows = _db.Query<TwFundFlowDaily>(
                "SELECT stock_code, trade_date, foreign_net FROM tw_fund_flow_daily WHERE trade_date >= @min AND trade_date <= @d",
                new { min = minDate, d = reportDate });

            foreach (var g in rows.GroupBy(r => r.StockCode))
                result[g.Key] = g.OrderByDescending(r => r.TradeDate).Select(r => r.ForeignNet).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: load history failed"); }
        return result;
    }

    /// <summary>產業層級的法人「連續同向」:把歷史每日各股的外資淨股(股)按產業加總 → sector→[淨額 最近在前]。
    /// 只用上市(DB 內)資料、上櫃不入歷史;sectorMap 涵蓋的代碼才算得到。給「產業連 N 日買/賣」用。</summary>
    private Dictionary<string, List<long>> LoadSectorForeignHistory(string reportDate, int k, Dictionary<string, string> sectorMap, Dictionary<string, decimal> closes)
    {
        var result = new Dictionary<string, List<long>>();
        if (sectorMap.Count == 0 || closes.Count == 0) return result;
        try
        {
            // 故意「不含報表日」(< @d):報表日當天的外資淨額由 caller 用 live reportRows prepend,
            // 避免 DB 還沒寫入今天時、consec 用到不含今天的序列、跟畫面外資方向打架。
            var dates = _db.Query<TwFundFlowDaily>(
                "SELECT DISTINCT trade_date FROM tw_fund_flow_daily WHERE trade_date < @d ORDER BY trade_date DESC LIMIT @k",
                new { d = reportDate, k })
                .Select(r => r.TradeDate).ToList();
            if (dates.Count == 0) return result;
            string minDate = dates.Min()!;

            var rows = _db.Query<TwFundFlowDaily>(
                "SELECT stock_code, trade_date, foreign_net FROM tw_fund_flow_daily WHERE trade_date >= @min AND trade_date < @d",
                new { min = minDate, d = reportDate });

            var bySectorDate = new Dictionary<string, Dictionary<string, long>>();
            foreach (var r in rows)
            {
                if (!(r.StockCode.Length == 4 && r.StockCode[0] != '0')) continue;   // 排 ETF
                if (!sectorMap.TryGetValue(r.StockCode, out var sec)) continue;
                if (!closes.TryGetValue(r.StockCode, out var px) || px <= 0) continue;   // 現價加權 → consec 跟畫面「億」同號
                if (!bySectorDate.TryGetValue(sec, out var dm)) { dm = new(); bySectorDate[sec] = dm; }
                dm.TryGetValue(r.TradeDate, out var cur);
                dm[r.TradeDate] = cur + (long)(r.ForeignNet * px / 100000m);   // 值加權(千元級)、只取號
            }
            var orderedDesc = dates.OrderByDescending(d => d).ToList();   // 最近在前
            foreach (var (sec, dm) in bySectorDate)
                result[sec] = orderedDesc.Where(dm.ContainsKey).Select(d => dm[d]).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: load sector history failed"); }
        return result;
    }

    // 個股→產業名 對照快取(少變、快取 12h、避免每次手動推都重抓 ~1090 家)
    private Dictionary<string, string>? _industryMap;
    private DateTime _industryMapAt = DateTime.MinValue;

    /// <summary>取得個股→中文產業名對照(快取 12h);抓失敗保留舊快取、再不行回空(產業段就略過)。</summary>
    private async Task<Dictionary<string, string>> GetIndustryMapAsync(HttpClient http, CancellationToken ct)
    {
        if (_industryMap is { Count: > 0 } && DateTime.UtcNow - _industryMapAt < TimeSpan.FromHours(12))
            return _industryMap;
        var m = await TwseFundFlowClient.FetchIndustryMapAsync(http, ct);
        if (m.Count > 0) { _industryMap = m; _industryMapAt = DateTime.UtcNow; }
        return _industryMap ?? new Dictionary<string, string>();
    }

    private Dictionary<string, string>? _otcIndustryMap;
    private DateTime _otcIndustryMapAt = DateTime.MinValue;

    /// <summary>取得上櫃個股→中文產業名對照(快取 12h)。</summary>
    private async Task<Dictionary<string, string>> GetTpexIndustryMapAsync(HttpClient http, CancellationToken ct)
    {
        if (_otcIndustryMap is { Count: > 0 } && DateTime.UtcNow - _otcIndustryMapAt < TimeSpan.FromHours(12))
            return _otcIndustryMap;
        var m = await TpexFundFlowClient.FetchIndustryMapAsync(http, ct);
        if (m.Count > 0) { _otcIndustryMap = m; _otcIndustryMapAt = DateTime.UtcNow; }
        return _otcIndustryMap ?? new Dictionary<string, string>();
    }

    /// <summary>讀「報表日的前一個交易日」DB 收盤(close_price&gt;0)→ code→close。算漲跌%用。</summary>
    private Dictionary<string, decimal> LoadPrevCloses(string isoDate)
    {
        var result = new Dictionary<string, decimal>();
        try
        {
            var prev = _db.Query<TwFundFlowDaily>(
                "SELECT DISTINCT trade_date FROM tw_fund_flow_daily WHERE trade_date < @d ORDER BY trade_date DESC LIMIT 1",
                new { d = isoDate }).FirstOrDefault()?.TradeDate;
            if (string.IsNullOrEmpty(prev)) return result;
            foreach (var r in _db.Query<TwFundFlowDaily>(
                "SELECT stock_code, close_price FROM tw_fund_flow_daily WHERE trade_date=@d AND close_price > 0", new { d = prev }))
                result[r.StockCode] = r.ClosePrice;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: load prev closes failed"); }
        return result;
    }

    /// <summary>讀某交易日 DB 既存收盤(close_price&gt;0)→ code→close。MI_INDEX 抓不到時的 fallback。</summary>
    private Dictionary<string, decimal> LoadStoredCloses(string isoDate)
    {
        var result = new Dictionary<string, decimal>();
        try
        {
            var rows = _db.Query<TwFundFlowDaily>(
                "SELECT stock_code, close_price FROM tw_fund_flow_daily WHERE trade_date=@d AND close_price > 0",
                new { d = isoDate });
            foreach (var r in rows) result[r.StockCode] = r.ClosePrice;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: load stored closes failed"); }
        return result;
    }

    /// <summary>冪等存檔:同交易日先 DELETE 再 Insert 全量。整批包 transaction。</summary>
    private void StoreRows(string isoDate, List<TwFundFlowDaily> rows)
    {
        try
        {
            _db.InTransaction(() =>
            {
                _db.Execute("DELETE FROM tw_fund_flow_daily WHERE trade_date = @d", new { d = isoDate });
                foreach (var r in rows) _db.Insert(r);
            });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: store failed for {Date}", isoDate); }
    }

    private void WriteHtml(string path, string html)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, html);
            _logger.LogDebug("TwFundFlow: HTML written to {Path} ({Bytes}B)", path, html.Length);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "TwFundFlow: HTML write failed ({Path})", path); }
    }

    private HttpClient CreateHttp()
    {
        var http = _httpFactory.CreateClient("twse-fundflow");
        http.Timeout = TimeSpan.FromSeconds(30);
        if (!http.DefaultRequestHeaders.Contains("User-Agent"))
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; b4a-fundflow/1.0)");
        return http;
    }

    private static int ParseIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
}
