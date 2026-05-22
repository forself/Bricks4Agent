using System;
using System.Collections.Generic;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// BollingerStrategy 變成「可被 walk-forward 優化」的契約測試:
/// ParamSchema 暴露三個可調參數,且 Evaluate 確實從 config.Params 讀(不是寫死常數)。
/// </summary>
public class BollingerParamSchemaTests
{
    private static List<BarData> Series(int count)
    {
        var bars = new List<BarData>();
        decimal price = 100m;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            decimal open = price;
            decimal osc = (i % 2 == 0) ? 2.0m : -2.0m;   // 製造波動,避免一直 squeeze
            decimal close = open + osc;
            bars.Add(new BarData
            {
                OpenTime = t0.AddHours(i * 4), Open = open,
                High = Math.Max(open, close) + 1m, Low = Math.Min(open, close) - 1m,
                Close = close, Volume = 1_000_000,
            });
            price = close;
        }
        return bars;
    }

    [Fact]
    public void ParamSchema_ExposesTunableParams()
    {
        var schema = new BollingerStrategy().ParamSchema;
        schema.Keys.Should().Contain(new[] { "bb_period", "bb_k_sigma", "bb_squeeze_threshold" });
    }

    [Fact]
    public void SqueezeThreshold_IsReadFromConfigParams()
    {
        var strat = new BollingerStrategy();
        var bars = Series(60);

        // 把 squeeze 門檻拉到 999% → 帶寬一定小於它 → 永遠判定 squeeze → 不進場(hold)。
        // 若 Evaluate 還在用寫死的常數 3,這個 override 不會生效、就可能不是 hold。
        var cfg = new StrategyConfig
        {
            Symbol = "X", Exchange = "test", Interval = "4h",
            Params = new Dictionary<string, object> { ["bb_squeeze_threshold"] = 999m },
        };

        strat.Evaluate(bars, cfg).Action.Should().Be("hold");
    }

    [Fact]
    public void DefaultConfig_ProducesValidAction()
    {
        var action = new BollingerStrategy()
            .Evaluate(Series(60), new StrategyConfig { Symbol = "X", Exchange = "test", Interval = "4h" })
            .Action;
        action.Should().BeOneOf("buy", "sell", "hold");
    }
}
