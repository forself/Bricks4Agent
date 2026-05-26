using System.Text.Json;
using Broker.Services;
using StrategyWorker.Engine;

namespace Unit.Tests.Smoke;

/// <summary>
/// portfolio.json validator(2026-05-26):build 時就抓到 typo / 不存在的策略名稱 / 缺欄位,
/// 不讓壞 config 流到 broker 啟動才發現。
///
/// 驗證:
///   1. JSON 能 parse(語法正確、註解 OK)
///   2. 每個 entry 的 Strategy 名稱在 strategy-worker 的 known registry 內(typo 防呆)
///   3. 必要欄位齊全(Symbol、Strategy、Mode、Leverage、Quantity > 0)
///   4. shadow=false 的 entry 標記為 ⚠(真錢、提醒人工檢查)
///
/// 「known registry」用 strategy-worker Program.cs 的同一份 dict 重建(若策略新增、
///  在這裡也加上,單一變更點)。composite(decorr4_ls 等)在 dict 後組裝。
/// </summary>
public class PortfolioJsonTests
{
    private static readonly string PortfolioPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "broker", "portfolio.json");

    /// <summary>strategy-worker 啟動時註冊的所有策略名(與 Program.cs 同步)。
    /// 加新部署策略時這裡也要加,避免漏防。</summary>
    private static HashSet<string> KnownStrategies()
    {
        // 鏡像 strategy-worker/Program.cs 的 strategies dict 鍵(2026-05-26 同步)
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 基礎策略
            "sma_cross", "macd_basic", "rsi_basic", "vwap",
            "supertrend", "stochastic", "atr_breakout", "vwap_revert",
            "mfi", "rsi_stoch", "smc",
            // ts_momentum 家族 / 趨勢
            "ts_momentum", "chandelier_trend", "ma_regime_trend", "dual_thrust", "accel_momentum",
            // 多空(LS)第一+第二批
            "dual_mom_ls", "di_trend_ls", "supertrend_ls", "bb_revert_ls", "donchian_fade_ls",
            // 形態工具
            "fib_retrace_ls", "harmonic_ls",
            // 8 年深日線新批
            "don_trend", "rsi2_rev", "boll_rev",
            // 2026-05-25 paper 驗證中
            "squeeze_breakout", "rsi_divergence", "volume_breakout",
            // composite
            "decorr4_ls", "osc_ensemble",
            // LLM(條件啟用、portfolio 通常不引用)
            "llm", "news_sentiment",
        };
        return names;
    }

    [Fact]
    public void PortfolioJson_ParsesAndValidates()
    {
        var path = PortfolioPath;
        Assert.True(File.Exists(path), $"portfolio.json 不存在: {path}");

        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<List<PortfolioReconciler.Entry>>(json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        Assert.NotNull(entries);
        Assert.NotEmpty(entries);

        var known = KnownStrategies();
        var errors = new List<string>();
        var liveWarnings = new List<string>();

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var ctx = $"entry[{i}] {e.Exchange}:{e.Symbol}:{e.Strategy}";

            // 必要欄位
            if (string.IsNullOrWhiteSpace(e.Symbol))   errors.Add($"{ctx}: Symbol 空");
            if (string.IsNullOrWhiteSpace(e.Strategy)) errors.Add($"{ctx}: Strategy 空");
            if (e.Quantity <= 0m)                       errors.Add($"{ctx}: Quantity={e.Quantity}(必須 > 0)");
            if (e.Leverage <= 0)                        errors.Add($"{ctx}: Leverage={e.Leverage}(必須 > 0)");

            // 策略名必須在已知列表內(typo 防呆)
            if (!string.IsNullOrWhiteSpace(e.Strategy) && !known.Contains(e.Strategy))
                errors.Add($"{ctx}: Strategy '{e.Strategy}' 不在 known registry,可能是 typo 或忘了同步測試");

            // Mode 必須是已知值
            var validModes = new[] { "perp_long_only", "perp_both", "spot" };
            if (!string.IsNullOrWhiteSpace(e.Mode) && !validModes.Contains(e.Mode))
                errors.Add($"{ctx}: Mode '{e.Mode}' 不在允許清單 {string.Join("/", validModes)}");

            // shadow=false 是真錢、記錄警示(不算錯誤,但測試輸出要看得到)
            if (!e.Shadow && e.Enabled)
                liveWarnings.Add($"⚠ LIVE: {ctx} lev={e.Leverage}x qty={e.Quantity}");
        }

        // 印 live 警示(測試輸出 stdout)
        if (liveWarnings.Count > 0)
        {
            Console.WriteLine($"\n=== portfolio.json LIVE entries({liveWarnings.Count}個、shadow=false 真錢)===");
            foreach (var w in liveWarnings) Console.WriteLine(w);
            Console.WriteLine("(這些不是錯誤,但每次改 portfolio.json 都該手動 review)\n");
        }

        Assert.True(errors.Count == 0,
            $"portfolio.json 驗證失敗 ({errors.Count} 個錯誤):\n  " + string.Join("\n  ", errors));
    }

    [Fact]
    public void PortfolioJson_AllowsCommentsAndTrailingCommas()
    {
        // 確保 JSON parser 設定容忍 portfolio.json 的人類友善格式(註解 + trailing commas)
        var sample = """
        // 這是註解
        [
            { "exchange": "bingx", "symbol": "BTC", "strategy": "dual_mom_ls", "quantity": 0.01, "leverage": 5, "shadow": true, },  // trailing comma
        ]
        """;
        var entries = JsonSerializer.Deserialize<List<PortfolioReconciler.Entry>>(sample,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        Assert.Single(entries!);
        Assert.Equal("dual_mom_ls", entries![0].Strategy);
    }
}
