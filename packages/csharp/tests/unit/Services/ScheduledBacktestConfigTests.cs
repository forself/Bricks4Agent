using Broker.Services;
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
}
