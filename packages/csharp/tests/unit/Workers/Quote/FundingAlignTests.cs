using FluentAssertions;
using QuoteWorker.Handlers;
using QuoteWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Quote;

/// <summary>
/// QuoteOhlcvHandler.AlignFunding 的純函式測試:把 8h 一次的 funding 序列 as-of join 到每根 K 線
/// (向前填充)。這是讓 strategy-worker 的 character_ensemble funding 維度有資料的關鍵接線。
/// </summary>
public class FundingAlignTests
{
    private static OhlcvBar Bar(int day) => new()
    {
        Symbol = "BTC", Interval = "1d",
        OpenTime = new DateTime(2024, 1, day, 0, 0, 0, DateTimeKind.Utc),
        Open = 100, High = 101, Low = 99, Close = 100, Volume = 1000,
    };

    private static FundingRatePoint Fund(int day, int hour, decimal rate) => new()
    {
        Symbol = "BTC",
        FundingTime = new DateTime(2024, 1, day, hour, 0, 0, DateTimeKind.Utc),
        FundingRate = rate,
    };

    [Fact]
    public void ForwardFills_NearestPriorFunding()
    {
        var bars = new List<OhlcvBar> { Bar(1), Bar(2), Bar(3) };
        // funding:day1 00:00 = 0.001、day2 16:00 = 0.002(晚於 day2 bar 的 00:00)
        var fundings = new List<FundingRatePoint> { Fund(1, 0, 0.001m), Fund(2, 16, 0.002m) };

        var merged = QuoteOhlcvHandler.AlignFunding(bars, fundings);

        merged.Should().HaveCount(3);
        merged[0].FundingRate.Should().Be(0.001m);   // day1 bar:取 day1 00:00
        merged[1].FundingRate.Should().Be(0.001m);   // day2 00:00 bar:day2 16:00 還沒發生 → 仍是 day1
        merged[2].FundingRate.Should().Be(0.002m);   // day3 bar:day2 16:00 已發生
    }

    [Fact]
    public void BarsBeforeAnyFunding_GetNull()
    {
        var bars = new List<OhlcvBar> { Bar(1), Bar(2) };
        var fundings = new List<FundingRatePoint> { Fund(2, 0, 0.001m) };  // 第一筆 funding 在 day2

        var merged = QuoteOhlcvHandler.AlignFunding(bars, fundings);

        merged[0].FundingRate.Should().BeNull();      // day1 早於任何 funding → null(strategy 端降級)
        merged[1].FundingRate.Should().Be(0.001m);
    }

    [Fact]
    public void NoFunding_AllNull()
        => QuoteOhlcvHandler.AlignFunding(
                new List<OhlcvBar> { Bar(1), Bar(2) }, new List<FundingRatePoint>())
            .Should().OnlyContain(m => m.FundingRate == null);
}
