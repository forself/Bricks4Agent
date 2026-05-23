using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 組合對帳(config-as-code)——啟動時依 portfolio.json 把「該有的 shadow watch」補齊。
/// 單一管理處:整個影子驗證組合寫在一個 repo 檔案裡(版本控制、Claude 可直接編輯 + commit)。
///
/// 安全鐵則(絕不違反):
///   1. 只自動建 shadow=true 的 watch —— **永遠不自動建 live(真錢)watch**(shadow=false 的 entry 一律略過、log 提醒手動)。
///   2. 不覆寫既有 watch(同 key 已存在就略過 → 不會蓋掉手動或 live 的)。
///   3. 不刪除任何 watch(只新增缺的)。
///   → 結論:這個檔案最多只能「多開幾個影子單」,動不到真錢、毀不了現有部位。真錢開火永遠在 dashboard 手動。
///
/// key 目前是 exchange:symbol(一個 symbol 一個策略);composite key(同 symbol 多策略)是後續第二步。
/// 所以這裡 shadow 驗證的策略請放「沒有 live watch 的 symbol」。
/// </summary>
public static class PortfolioReconciler
{
    public sealed class Entry
    {
        public string Exchange { get; set; } = "bingx";
        public string Symbol { get; set; } = "";
        public string Strategy { get; set; } = "";
        public string Mode { get; set; } = "perp_long_only";
        public int Leverage { get; set; } = 5;
        public decimal Quantity { get; set; } = 0m;
        public bool Shadow { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public string? HtfInterval { get; set; }
        public string? Owner { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>讀 BaseDirectory/portfolio.json 並套用。檔案不存在 = 略過(非錯誤)。</summary>
    public static void Apply(AutoTraderService svc, ILogger logger)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "portfolio.json");
        if (!File.Exists(path)) { logger.LogInformation("PortfolioReconciler: no portfolio.json at {Path}, skip", path); return; }
        try { Apply(svc, File.ReadAllText(path), logger); }
        catch (Exception ex) { logger.LogWarning(ex, "PortfolioReconciler: failed to apply portfolio.json"); }
    }

    /// <summary>核心(吃 JSON 字串、可單測)。回傳 (added, skippedLive, skippedExisting)。</summary>
    public static (int added, int skippedLive, int skippedExisting) Apply(AutoTraderService svc, string json, ILogger logger)
    {
        var entries = JsonSerializer.Deserialize<List<Entry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? new List<Entry>();

        int added = 0, skippedLive = 0, skippedExisting = 0;
        foreach (var e in entries)
        {
            if (!e.Enabled || string.IsNullOrWhiteSpace(e.Symbol) || string.IsNullOrWhiteSpace(e.Strategy)) continue;

            // 鐵則 1:絕不自動建真錢單
            if (!e.Shadow)
            {
                skippedLive++;
                logger.LogInformation("PortfolioReconciler: SKIP live entry {Ex}:{Sym}:{Strat} — live 必須在 dashboard 手動加,config 不自動武裝真錢",
                    e.Exchange, e.Symbol, e.Strategy);
                continue;
            }

            // 鐵則 2:不覆寫既有
            var key = $"{e.Exchange}:{e.Symbol}";
            if (svc.WatchList.ContainsKey(key))
            {
                skippedExisting++;
                logger.LogInformation("PortfolioReconciler: SKIP {Key} — 已有 watch、不覆寫", key);
                continue;
            }

            svc.AddWatch(e.Symbol, e.Exchange, e.Strategy, e.Quantity, e.Mode, e.Leverage,
                string.IsNullOrEmpty(e.Owner) ? "prn_dashboard" : e.Owner, e.HtfInterval, shadow: true);
            added++;
            logger.LogInformation("PortfolioReconciler: ADD 👻 shadow watch {Ex}:{Sym} strategy={Strat} {Mode} {Lev}x{Note}",
                e.Exchange, e.Symbol, e.Strategy, e.Mode, e.Leverage, string.IsNullOrEmpty(e.Note) ? "" : $" ({e.Note})");
        }
        logger.LogInformation("PortfolioReconciler: done — added {Added} shadow, skipped {Live} live, {Exist} existing",
            added, skippedLive, skippedExisting);
        return (added, skippedLive, skippedExisting);
    }
}
