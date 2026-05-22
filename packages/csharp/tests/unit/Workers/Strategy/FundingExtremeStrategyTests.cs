using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// funding_extreme：資金費率極端反轉(contrarian)的訊號契約。
///   - funding 在近期極低端 → buy(空頭擁擠、軋空偏多)
///   - 極高端 → sell(多頭擁擠、出場)
///   - 無 funding 資料 → hold(自動降級、非 perp 不誤觸)
/// </summary>
public class FundingExtremeStrategyTests
{
    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };

    /// <summary>60 根平盤 K 線;funding 前 59 根 = baseline、最後一根 = last。null 代表不帶 funding。</summary>
    private static List<BarData> Bars(decimal baseline, decimal? last, bool withFunding = true)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(60);
        for (int i = 0; i < 60; i++)
        {
            decimal? f = withFunding ? (i == 59 ? last : baseline) : null;
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1000m,
                FundingRate = f,
            });
        }
        return bars;
    }

    [Fact]
    public void ExtremeLowFunding_CrowdedShorts_Buys()
    {
        // 多數 +0.0001、最後一根 -0.005（極低端）→ 百分位最低 → buy
        var r = new FundingExtremeStrategy().Evaluate(Bars(0.0001m, -0.005m), Cfg());
        r.Action.Should().Be("buy");
        r.Confidence.Should().BeGreaterThanOrEqualTo(0.6m);   // 要 ≥0.6 引擎才會進場
    }

    [Fact]
    public void ExtremeHighFunding_CrowdedLongs_Sells()
    {
        var r = new FundingExtremeStrategy().Evaluate(Bars(0.0001m, 0.005m), Cfg());
        r.Action.Should().Be("sell");
    }

    [Fact]
    public void NeutralFunding_Holds()
    {
        // funding 線性遞增、最後一根設成中位(rank ~30/60、百分位 ~0.53)→ 落在中性區 → hold
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(60);
        for (int i = 0; i < 60; i++)
        {
            decimal f = (i == 59 ? 30 : i) * 0.00001m;
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1000m,
                FundingRate = f,
            });
        }
        var r = new FundingExtremeStrategy().Evaluate(bars, Cfg());
        r.Action.Should().Be("hold");
    }

    [Fact]
    public void NoFundingData_Holds()
    {
        var r = new FundingExtremeStrategy().Evaluate(Bars(0m, 0m, withFunding: false), Cfg());
        r.Action.Should().Be("hold");
        r.Reason.Should().Contain("funding");
    }
}
