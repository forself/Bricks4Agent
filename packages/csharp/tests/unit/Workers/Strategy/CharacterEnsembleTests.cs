using System.Linq;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// CharacterAdaptiveEnsemble 測試:它把 Hurst+波動率當 meta 閘門去調成員權重。
/// 重點驗證:可被優化器掃參、資料不足回 hold、輸出合法訊號 + meta 特徵、
/// 以及成員權重經正規化(總和≈1,確認閘門乘數有被 normalize 不會爆掉)。
/// </summary>
public class CharacterEnsembleTests
{
    [Fact]
    public void IsOptimizable()
        => CharacterAdaptiveEnsembleStrategy.DefaultFrom(Reg()).ParamSchema.Keys
            .Should().Contain(new[] { "char_trend_th", "char_meanrev_th" });

    [Fact]
    public void TooFewBars_Holds()
        => CharacterAdaptiveEnsembleStrategy.DefaultFrom(Reg())
            .Evaluate(Series(30), Cfg()).Action.Should().Be("hold");

    [Fact]
    public void ProducesValidSignal()
    {
        var sig = CharacterAdaptiveEnsembleStrategy.DefaultFrom(Reg()).Evaluate(Series(160), Cfg());
        sig.Action.Should().BeOneOf("buy", "sell", "hold");
        sig.Confidence.Should().BeInRange(0m, 1m);
    }

    [Fact]
    public void ExposesMetaFeatures()
    {
        var sig = CharacterAdaptiveEnsembleStrategy.DefaultFrom(Reg()).Evaluate(Series(160), Cfg());
        sig.Indicators.Should().ContainKey("hurst");
        sig.Indicators.Should().ContainKey("vol_percentile");
        sig.Indicators.Should().ContainKey("character");
        sig.Indicators.Should().ContainKey("skew");
        sig.Indicators.Should().ContainKey("kurtosis");
        sig.Indicators.Should().ContainKey("tail_risk");
    }

    [Fact]
    public void MemberWeightsNormalized()
    {
        var sig = CharacterAdaptiveEnsembleStrategy.DefaultFrom(Reg()).Evaluate(Series(160), Cfg());
        var wsum = sig.Indicators.Where(kv => kv.Key.StartsWith("weight.")).Sum(kv => kv.Value);
        wsum.Should().BeApproximately(1m, 0.01m);
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static StrategyConfig Cfg() => new() { Symbol = "X", Exchange = "test", Interval = "4h" };

    /// <summary>含跨類別成員的 registry,讓 DefaultFrom 挑得到 Trend/MeanReversion/Breakout/... 各類。</summary>
    private static Dictionary<string, IStrategy> Reg() => new()
    {
        ["sma_cross"]       = new SmaCrossStrategy(),
        ["super_trend"]     = new SuperTrendStrategy(),
        ["macd_divergence"] = new MacdStrategy(),
        ["rsi_oversold"]    = new RsiStrategy(),
        ["bollinger_bands"] = new BollingerStrategy(),
        ["donchian"]        = new DonchianStrategy(),
        ["multi_timeframe"] = new MultiTimeframeStrategy(),
        ["obv"]             = new ObvStrategy(),
        ["harmonic_pattern"] = new HarmonicStrategy(),
    };

    /// <summary>溫和上升 + 正弦波動的合成序列(有 OHLC,讓各指標都算得出)。</summary>
    private static List<BarData> Series(int n)
    {
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal price = 100m;
        for (int i = 0; i < n; i++)
        {
            decimal open = price;
            decimal close = 100m + i * 0.3m + (decimal)Math.Sin(i / 6.0) * 1.5m;
            bars.Add(new BarData
            {
                OpenTime = t0.AddHours(i * 4),
                Open = open,
                High = Math.Max(open, close) + 1m,
                Low  = Math.Min(open, close) - 1m,
                Close = close,
                Volume = 1_000_000m,
            });
            price = close;
        }
        return bars;
    }
}
