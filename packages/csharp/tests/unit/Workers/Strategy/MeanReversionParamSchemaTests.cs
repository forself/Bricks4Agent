using System;
using System.Collections.Generic;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// CCI / MFI / Keltner / Stochastic 變成「可被 walk-forward 優化」的契約測試:
/// 各暴露對應 ParamSchema,且 Evaluate 確實從 config.Params 讀(把週期參數設超大 → 指標算不出 → Hold,
/// 證明參數有被讀取、不是寫死)。
/// </summary>
public class MeanReversionParamSchemaTests
{
    private static List<BarData> Series(int n = 60)
    {
        var bars = new List<BarData>();
        decimal p = 100m;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            decimal o = p, osc = (i % 2 == 0) ? 2m : -2m, c = o + osc;
            bars.Add(new BarData
            {
                OpenTime = t0.AddHours(i * 4), Open = o,
                High = Math.Max(o, c) + 1m, Low = Math.Min(o, c) - 1m, Close = c,
                Volume = 1_000_000 + i * 1000,
            });
            p = c;
        }
        return bars;
    }

    private static StrategyConfig Cfg(string key, object val) => new()
    {
        Symbol = "X", Exchange = "test", Interval = "4h",
        Params = new Dictionary<string, object> { [key] = val },
    };

    [Fact]
    public void Cci_Optimizable_AndReadsPeriod()
    {
        var s = new CciStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "cci_period", "cci_threshold" });
        s.Evaluate(Series(), Cfg("cci_period", 9999)).Action.Should().Be("hold");
    }

    [Fact]
    public void Mfi_Optimizable_AndReadsPeriod()
    {
        var s = new MfiStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "mfi_period", "mfi_oversold", "mfi_overbought" });
        s.Evaluate(Series(), Cfg("mfi_period", 9999)).Action.Should().Be("hold");
    }

    [Fact]
    public void Keltner_Optimizable_AndReadsPeriod()
    {
        var s = new KeltnerStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "keltner_period", "keltner_atr_mult" });
        s.Evaluate(Series(), Cfg("keltner_period", 9999)).Action.Should().Be("hold");
    }

    [Fact]
    public void Stochastic_Optimizable_AndReadsPeriod()
    {
        var s = new StochasticStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "rsi_period", "stoch_k", "stoch_d" });
        s.Evaluate(Series(), Cfg("stoch_k", 9999)).Action.Should().Be("hold");
    }
}
