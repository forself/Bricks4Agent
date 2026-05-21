using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// SignalFeedEngine(多維訊號雷達卡)契約測試:
/// bar 不足回 null;6 維雷達齊全且數值落在 0-100;方向/星等/平均勝率契約;MTF 為選用維度。
/// </summary>
public class SignalFeedEngineTests
{
    private static List<BarData> Series(int count, decimal start, decimal drift, decimal wiggle)
    {
        var bars = new List<BarData>();
        decimal price = start;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            decimal open = price;
            decimal osc = (i % 2 == 0) ? wiggle : -wiggle;
            decimal close = open + drift + osc;
            decimal high = Math.Max(open, close) + wiggle;
            decimal low = Math.Min(open, close) - wiggle;
            bars.Add(new BarData
            {
                OpenTime = t0.AddHours(i * 4), Open = open, High = high, Low = low, Close = close,
                Volume = 1_000_000 + i * 1000,
            });
            price = close;
        }
        return bars;
    }

    [Fact]
    public void Build_TooFewBars_ReturnsNull()
    {
        SignalFeedEngine.Build("X", Series(SignalFeedEngine.MinBars - 1, 100m, 0.3m, 0.5m))
            .Should().BeNull();
    }

    [Fact]
    public void Build_ProducesValidCard()
    {
        var card = SignalFeedEngine.Build("BTCUSDT", Series(80, 100m, 0.6m, 1.2m));

        card.Should().NotBeNull();
        card!.Direction.Should().BeOneOf("bullish", "bearish", "neutral");
        card.Confidence.Should().BeInRange(0, 100);
        card.Stars.Should().BeInRange(1, 5);
        card.CurrentPrice.Should().BeGreaterThan(0);

        // 6 維雷達都在;沒給 MTF/funding 的維度為 null,其餘(bars 足夠)有值且 0-100
        card.Radar.Keys.Should().BeEquivalentTo(new[]
        {
            "signal_strength", "trend_consistency", "momentum", "volume", "risk_reward", "funding"
        });
        card.Radar["trend_consistency"].Should().BeNull(); // 沒給 MTF
        card.Radar["funding"].Should().BeNull();           // 沒給 funding
        foreach (var v in card.Radar.Values.Where(v => v.HasValue))
            v!.Value.Should().BeInRange(0, 100);
    }

    [Fact]
    public void Build_WithMtf_FillsTrendConsistency()
    {
        var card = SignalFeedEngine.Build("ETHUSDT", Series(80, 100m, 0.6m, 1.2m),
            mtfBullish: 3, mtfBearish: 0, mtfTotal: 3);

        card.Should().NotBeNull();
        card!.Radar["trend_consistency"].Should().NotBeNull();
        card.Radar["trend_consistency"]!.Value.Should().BeInRange(20, 95);
    }
}
