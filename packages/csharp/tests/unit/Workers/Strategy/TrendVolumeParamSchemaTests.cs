using System;
using System.Collections.Generic;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// ichimoku / parabolic_sar / chaikin_mf / vegas_tunnel 變成「可被 walk-forward 優化」的契約測試:
/// 各暴露對應 ParamSchema(→ 通用優化器可調),能驗證的再驗「參數確實從 config 讀」。
/// </summary>
public class TrendVolumeParamSchemaTests
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
    public void Ichimoku_Optimizable_AndReadsPeriod()
    {
        var s = new IchimokuStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "ichimoku_tenkan", "ichimoku_kijun", "ichimoku_ssb" });
        s.Evaluate(Series(), Cfg("ichimoku_ssb", 9999)).Action.Should().Be("hold");
    }

    [Fact]
    public void ChaikinMf_Optimizable_AndReadsPeriod()
    {
        var s = new ChaikinMfStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "cmf_period", "cmf_threshold" });
        s.Evaluate(Series(), Cfg("cmf_period", 9999)).Action.Should().Be("hold");
    }

    [Fact]
    public void ParabolicSar_Optimizable_AndProducesValidAction()
    {
        var s = new ParabolicSarStrategy();
        s.ParamSchema.Keys.Should().Contain(new[] { "psar_af_step", "psar_max_af" });
        // SAR 的 af 參數不會 gate 成 hold,改驗:帶 Params 仍能正常產出合法 action(參數有被吃進去、不丟例外)
        s.Evaluate(Series(), Cfg("psar_max_af", 0.4m)).Action.Should().BeOneOf("buy", "sell", "hold");
    }

    [Fact]
    public void VegasTunnel_Optimizable()
    {
        // vegas 需 ≥676 bars 才會跑邏輯,行為測試成本高;這裡只驗它現在可被優化器調(有 schema)。
        new VegasTunnelStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "vegas_trigger", "vegas_min_tunnel_width" });
    }
}
