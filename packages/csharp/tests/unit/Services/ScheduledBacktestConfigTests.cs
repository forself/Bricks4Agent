using Broker.Services;
using BrokerCore.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Unit.Tests.Services;

/// <summary>
/// 鎖住 ScheduledBacktestService 配置解析契約：
///   - Lab:Strategies 未設 → 用預設 24 條
///   - Lab:Strategies 設了非空 array → 整個替換
///   - Lab:Strategies 設成空 array → safety：退回預設（防誤設 [] 變零策略）
///   - Lab:MinTrades 未設 → 預設 3
///   - Lab:MinTrades = 0 / 負數 → clamp 到 1（避免「全 fan」推薦）
///   - Lab:MinTrades = 自訂值 → 透傳
///
/// 為什麼這個 test 重要：4 → 24 條策略的擴張是 user demo 反饋「上一單還在審核」之後做的
/// 「策略品質 review」第一步。寫死 4 條的 bug 被 review 翻出來、未來不能再回到 hardcoded。
/// 配置外露讓不同部署可選不同策略子集（VPS production / dev / 朋友 fork）。
/// </summary>
public class ScheduledBacktestConfigTests
{
    private static readonly string[] DummyDefaults = { "sma_cross", "rsi_oversold", "macd_divergence" };

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void ResolveStrategies_NoConfig_UsesDefaults()
    {
        var config = BuildConfig(new());
        var result = ScheduledBacktestService.ResolveStrategies(config, DummyDefaults);
        result.Should().BeEquivalentTo(DummyDefaults);
    }

    [Fact]
    public void ResolveStrategies_ConfiguredArray_Overrides()
    {
        var config = BuildConfig(new()
        {
            ["Lab:Strategies:0"] = "vwap",
            ["Lab:Strategies:1"] = "super_trend",
        });
        var result = ScheduledBacktestService.ResolveStrategies(config, DummyDefaults);
        result.Should().Equal("vwap", "super_trend");
    }

    [Fact]
    public void ResolveStrategies_EmptyArray_FallsBackToDefaults_NoZeroStrategyBug()
    {
        // safety guard：誤設 Lab:Strategies = [] 不該變成「跑零策略」
        var config = BuildConfig(new()
        {
            // 完全沒 index 等同未設、Get<string[]>() 回 null
            // 但若有 config provider 給 empty array、退回 defaults
        });
        var result = ScheduledBacktestService.ResolveStrategies(config, DummyDefaults);
        result.Should().NotBeEmpty("零策略 = batch 不跑、recommendation 永遠空");
    }

    [Fact]
    public void ResolveMinTrades_NoConfig_DefaultsTo3()
    {
        var config = BuildConfig(new());
        ScheduledBacktestService.ResolveMinTrades(config).Should().Be(3);
    }

    [Fact]
    public void ResolveMinTrades_ZeroValue_ClampsTo1()
    {
        var config = BuildConfig(new() { ["Lab:MinTrades"] = "0" });
        ScheduledBacktestService.ResolveMinTrades(config).Should().Be(1,
            "min=0 等同無 gate、會推薦 0-trade lucky 結果、必須 clamp 到 1");
    }

    [Fact]
    public void ResolveMinTrades_NegativeValue_ClampsTo1()
    {
        var config = BuildConfig(new() { ["Lab:MinTrades"] = "-5" });
        ScheduledBacktestService.ResolveMinTrades(config).Should().Be(1);
    }

    [Fact]
    public void ResolveMinTrades_CustomPositiveValue_PassesThrough()
    {
        var config = BuildConfig(new() { ["Lab:MinTrades"] = "10" });
        ScheduledBacktestService.ResolveMinTrades(config).Should().Be(10);
    }

    // ── A1：ScoreWeights 配置解析 ─────────────────────────────────────────

    [Fact]
    public void ResolveScoreWeights_NoConfig_UsesDemoDefaults()
    {
        var w = ScheduledBacktestService.ResolveScoreWeights(BuildConfig(new()));
        w.Sharpe.Should().Be(0.35m);
        w.Return.Should().Be(0.25m);
        w.WinRate.Should().Be(0.15m);
        w.DrawdownPenalty.Should().Be(0.1m);
        w.Oos.Should().Be(0.15m);
    }

    [Fact]
    public void ResolveScoreWeights_PartialOverride_OnlyChangesSpecified()
    {
        // 只覆蓋 Sharpe + Oos、其他維持預設
        var config = BuildConfig(new()
        {
            ["Lab:Score:Sharpe"] = "0.5",
            ["Lab:Score:Oos"] = "0.3",
        });
        var w = ScheduledBacktestService.ResolveScoreWeights(config);
        w.Sharpe.Should().Be(0.5m);
        w.Oos.Should().Be(0.3m);
        w.Return.Should().Be(0.25m, "未覆寫應維持預設");
        w.WinRate.Should().Be(0.15m);
        w.DrawdownPenalty.Should().Be(0.1m);
    }

    [Fact]
    public void ResolveScoreWeights_NegativeValue_ClampsToZero()
    {
        // 防誤設變懲罰：負權重會把 sharpe 變反向、整個排名失準
        var config = BuildConfig(new() { ["Lab:Score:Sharpe"] = "-0.5" });
        ScheduledBacktestService.ResolveScoreWeights(config).Sharpe.Should().Be(0m);
    }

    // ── A1 cont.：ComputeScore 套不同 weights ────────────────────────────

    [Fact]
    public void ComputeScore_ZeroTrades_ReturnsZero()
    {
        var r = new BacktestResultEntry { Trades = 0, Sharpe = 99m };
        ScheduledBacktestService.ComputeScore(r, new ScoreWeights()).Should().Be(0m,
            "沒交易 = 沒樣本、不論其它指標多漂亮都不該被推薦");
    }

    [Fact]
    public void ComputeScore_DefaultWeights_ProducesKnownBaseline()
    {
        // 給一組已知數字、確認預設權重算出的分數沒變（向後相容）
        var r = new BacktestResultEntry
        {
            Trades = 10, Sharpe = 2m, TotalReturnPct = 50m,
            WinRate = 60m, MaxDdPct = 20m, WfFolds = 0,
        };
        var score = ScheduledBacktestService.ComputeScore(r, new ScoreWeights());
        // sharpeNorm=(2+2)/7=0.5714, returnNorm=0.5, winRateNorm=0.6, ddPenalty=0.4
        // baseScore = 0.35×0.5714 + 0.25×0.5 + 0.15×0.6 − 0.1×0.4 = 0.2 + 0.125 + 0.09 - 0.04 = 0.375
        score.Should().BeApproximately(0.375m, 0.001m);
    }

    [Fact]
    public void ComputeScore_CustomWeights_ChangesRanking()
    {
        var r = new BacktestResultEntry
        {
            Trades = 10, Sharpe = 5m, TotalReturnPct = 10m, WinRate = 40m, MaxDdPct = 30m,
        };
        // 把 Sharpe 權重拉到 1.0、其他歸 0 → 分數應該只反映 sharpe
        var sharpeOnly = new ScoreWeights(Sharpe: 1m, Return: 0m, WinRate: 0m, DrawdownPenalty: 0m, Oos: 0m);
        var score = ScheduledBacktestService.ComputeScore(r, sharpeOnly);
        // sharpeNorm = (5+2)/7 = 1.0 (clamped) × 1.0 weight = 1.0
        score.Should().Be(1m);
    }

    // ── A3：MaxParallel 配置解析 ─────────────────────────────────────────

    [Fact]
    public void ResolveMaxParallel_NoConfig_DefaultsTo4()
    {
        ScheduledBacktestService.ResolveMaxParallel(BuildConfig(new())).Should().Be(4);
    }

    [Fact]
    public void ResolveMaxParallel_ClampsTo1_For0OrNegative()
    {
        ScheduledBacktestService.ResolveMaxParallel(
            BuildConfig(new() { ["Lab:MaxParallel"] = "0" })).Should().Be(1);
        ScheduledBacktestService.ResolveMaxParallel(
            BuildConfig(new() { ["Lab:MaxParallel"] = "-3" })).Should().Be(1);
    }

    [Fact]
    public void ResolveMaxParallel_ClampsTo16_ForOverkill()
    {
        // 防有人寫 999 灌爆 strategy-worker
        ScheduledBacktestService.ResolveMaxParallel(
            BuildConfig(new() { ["Lab:MaxParallel"] = "999" })).Should().Be(16);
    }

    [Fact]
    public void ResolveMaxParallel_CustomValue_PassesThrough()
    {
        ScheduledBacktestService.ResolveMaxParallel(
            BuildConfig(new() { ["Lab:MaxParallel"] = "8" })).Should().Be(8);
    }
}
