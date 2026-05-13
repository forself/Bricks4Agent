using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 鎖住 ensemble 策略的核心契約：
///   - hold 視為棄權、不再稀釋 buy/sell（與 CompositeStrategy fix 同步）
///   - confidence = 同向票權重總和的平均信心
///   - 全員 hold → hold @ 0.3
///   - buy/sell 持平 → hold（不出單）
///   - indicators 帶 weight.* / vote.* / agreement_ratio 給下游判斷分歧
/// 用 stub strategies 跳過真實指標計算；BacktestEngine 對 stub 算 Sharpe 會 fallback
/// 到等權，所以 weights 對所有 stub 一樣，可以單純驗投票邏輯。
/// </summary>
public class WeightedEnsembleStrategyTests
{
    private sealed class StubStrategy : IStrategy
    {
        private readonly string _name;
        private readonly string _action;
        private readonly decimal _confidence;
        public StubStrategy(string name, string action, decimal confidence)
        { _name = name; _action = action; _confidence = confidence; }

        public string Name => _name;
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = _name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = _action, Confidence = _confidence, Reason = $"stub:{_action}",
            Interval = config.Interval,
        };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "AAPL", Exchange = "alpaca", Interval = "1d" };

    // 100 根「完全平盤」K 線 — 刻意讓 BacktestEngine 對所有 stub 都算出 Sharpe 0
    // （無價差→無 PnL→Sharpe 0 或 NaN→clamp 到 0），totalWeight=0 觸發等權 fallback
    // （ensemble 的設計：全員虧錢時退回等權，不讓 ensemble 完全靜音）。
    // 這樣 weights 對所有 stub 都是 1m，可以單純驗投票邏輯不被 Sharpe 偏置干擾。
    private static List<BarData> MakeBars(int count = 100)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count).Select(i => new BarData
        {
            OpenTime = t0.AddDays(i),
            Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1000m,
        }).ToList();
    }

    [Fact]
    public void OneBuy_TwoHold_ReturnsBuy_NotDilutedByHold()
    {
        // ensemble fallback to equal weights when Sharpe = 0 → 每個 stub 拿 1/3 weight
        // buyScore = 0.7 * (1/3) ≈ 0.233、buyWeight = 1/3
        // → confidence = 0.233 / (1/3) = 0.7 ✓
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "buy",  0.7m),
            new StubStrategy("s2", "hold", 0.5m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        signal.Action.Should().Be("buy");
        signal.Confidence.Should().Be(0.7m);
    }

    [Fact]
    public void AllHold_ReturnsHold_With_0_3_Confidence()
    {
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "hold", 0.5m),
            new StubStrategy("s2", "hold", 0.5m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        signal.Action.Should().Be("hold");
        signal.Confidence.Should().Be(0.3m, "all-neutral should give explicit low-conf hold sentinel");
    }

    [Fact]
    public void BuySellTied_ReturnsHold_NoTieBreaker()
    {
        // 用 echo stub：每個成員回的 action 跟內容都一樣 → BacktestEngine 對它們算出同樣
        // 的 Sharpe → totalWeight 同樣比例 → 確保 buy 跟 sell 拿到相同的 wNorm。
        // 用兩對 mirror（buy×2, sell×2）讓持平更可靠。
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "buy",  0.7m),
            new StubStrategy("b", "buy",  0.7m),
            new StubStrategy("c", "sell", 0.7m),
            new StubStrategy("d", "sell", 0.7m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        // 平盤 K 線：所有 stub 跑出 Sharpe = 0 → fallback 到等權；2 buy@0.7 vs 2 sell@0.7 → 同分 → hold
        signal.Action.Should().Be("hold", "tied buy/sell scores should not pick a side");
        signal.Confidence.Should().Be(0.3m);
    }

    [Fact]
    public void StrongerSell_BeatsWeakerBuy()
    {
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "buy",  0.5m),
            new StubStrategy("s2", "sell", 0.9m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        signal.Action.Should().Be("sell");
        signal.Confidence.Should().Be(0.9m);
    }

    [Fact]
    public void IndicatorsIncludeWeightVoteAndAgreementRatio()
    {
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "buy", 0.7m),
            new StubStrategy("s2", "buy", 0.6m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        // 每個 constituent 都有 weight.X 跟 vote.X
        signal.Indicators.Should().ContainKey("weight.s1");
        signal.Indicators.Should().ContainKey("weight.s2");
        signal.Indicators.Should().ContainKey("weight.s3");
        signal.Indicators["vote.s1"].Should().Be(1m, "buy 投票顯示為 +1");
        signal.Indicators["vote.s2"].Should().Be(1m);
        signal.Indicators["vote.s3"].Should().Be(0m, "hold 投票顯示為 0");

        // agreement_ratio：3 個成員裡有 2 個跟最終 action(buy) 同向 → 2/3
        signal.Indicators.Should().ContainKey("agreement_ratio");
        signal.Indicators["agreement_ratio"].Should().BeApproximately(0.6667m, 0.001m);

        // buy_score / sell_score 仍會輸出（hold_score 已移除）
        signal.Indicators.Should().ContainKey("buy_score");
        signal.Indicators.Should().ContainKey("sell_score");
        signal.Indicators.Should().NotContainKey("hold_score");
    }

    [Fact]
    public void ThrowsOnEmptyConstituents()
    {
        var act = () => new WeightedEnsembleStrategy(new List<IStrategy>());
        act.Should().Throw<ArgumentException>();
    }

    // ── Arbitrator hook (#3) ───────────────────────────────────────

    /// <summary>記錄是否被叫到、回傳指定的 signal（或 null 模擬 LLM 失敗）</summary>
    private sealed class StubArbitrator : IEnsembleArbitrator
    {
        public decimal Threshold { get; }
        public int Calls { get; private set; }
        public decimal LastAgreementRatio { get; private set; }
        public IReadOnlyList<Signal>? LastSignals { get; private set; }
        private readonly Signal? _toReturn;
        public StubArbitrator(decimal threshold, Signal? toReturn) { Threshold = threshold; _toReturn = toReturn; }
        public Signal? Arbitrate(IReadOnlyList<Signal> sigs, decimal agreementRatio, List<BarData> bars, StrategyConfig config)
        {
            Calls++; LastAgreementRatio = agreementRatio; LastSignals = sigs;
            return _toReturn;
        }
    }

    [Fact]
    public void Arbitrator_NotInvokedWhenAgreementHigh()
    {
        // 全員 buy → agreement = 1.0、threshold 0.6 → 不該叫 arbitrator
        var stub = new StubArbitrator(0.6m, toReturn: null);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "buy", 0.7m),
            new StubStrategy("b", "buy", 0.7m),
            new StubStrategy("c", "buy", 0.7m),
        }, arbitrator: stub);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        stub.Calls.Should().Be(0, "all-aligned ensemble should not pay LLM cost");
        sig.Action.Should().Be("buy");
    }

    [Fact]
    public void Arbitrator_InvokedWhenAgreementBelowThreshold()
    {
        // 4 個 constituent、1 buy / 3 sell → 主流（sell）agreement = 3/4 = 0.75
        // threshold 0.8 → 0.75 < 0.8 → 觸發
        var stub = new StubArbitrator(0.8m, toReturn: null);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "buy",  0.6m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.5m),
            new StubStrategy("d", "sell", 0.6m),
        }, arbitrator: stub);

        ensemble.Evaluate(MakeBars(), Cfg());

        stub.Calls.Should().Be(1);
        stub.LastSignals.Should().HaveCount(4);
        stub.LastAgreementRatio.Should().BeApproximately(0.75m, 0.01m);
    }

    [Fact]
    public void Arbitrator_OverridesActionWhenItReturnsSignal()
    {
        // 4 個 constituent、3 sell + 1 buy；arbitrator 強制回 buy → 最終 action = buy
        var override_ = new Signal
        {
            SignalId = "arb", Strategy = "ensemble", Symbol = "AAPL", Exchange = "alpaca",
            Action = "buy", Confidence = 0.85m, Reason = "LLM 看穿 short 的 reasoning 是雜訊", Interval = "1d",
        };
        var stub = new StubArbitrator(0.8m, toReturn: override_);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "sell", 0.7m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.6m),
            new StubStrategy("d", "buy",  0.5m),
        }, arbitrator: stub);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        sig.Action.Should().Be("buy", "arbitrator override should win over weighted vote");
        sig.Confidence.Should().Be(0.85m);
        sig.Reason.Should().Contain("LLM 看穿");
        sig.Indicators["arbitration.invoked"].Should().Be(1m);
        sig.Indicators["arbitration.result_action"].Should().Be(1m);
    }

    [Fact]
    public void Arbitrator_NullReturnFallsBackToWeightedVote()
    {
        // arbitrator 回 null（模擬 LLM 超時或解析失敗）→ 用原本的加權投票結果
        var stub = new StubArbitrator(0.8m, toReturn: null);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "sell", 0.7m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.6m),
            new StubStrategy("d", "buy",  0.5m),
        }, arbitrator: stub);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        stub.Calls.Should().Be(1);
        sig.Action.Should().Be("sell", "fallback to weighted vote when arbitrator declines");
        sig.Indicators.Should().ContainKey("arbitration.invoked");
        sig.Indicators.Should().NotContainKey("arbitration.result_action", "no override applied");
    }

    [Fact]
    public void Arbitrator_ThrowsExceptionFallsBackGracefully()
    {
        // 仲裁者拋例外 → 不該打死整個 evaluate
        var throwing = new ThrowingArbitrator(threshold: 0.8m);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "buy",  0.7m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.6m),
            new StubStrategy("d", "buy",  0.5m),
        }, arbitrator: throwing);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        sig.Should().NotBeNull();
        sig.Indicators.Should().ContainKey("arbitration.failed");
    }

    private sealed class ThrowingArbitrator : IEnsembleArbitrator
    {
        public decimal Threshold { get; }
        public ThrowingArbitrator(decimal threshold) { Threshold = threshold; }
        public Signal? Arbitrate(IReadOnlyList<Signal> sigs, decimal agreementRatio, List<BarData> bars, StrategyConfig config)
            => throw new InvalidOperationException("simulated LLM proxy failure");
    }

    // ── Audit indicators (#audit-gap mitigation) ────────────────────
    // 學術防禦：當 LLM arbitrator 覆蓋加權投票時，broker 要看得到「LLM 改了什麼」，
    // 不是只看到最終 action。下面三個 test 鎖住 fallback_would_be / changed_decision
    // 兩個 indicator、確保 dashboard 可以算「LLM 仲裁覆蓋率」。

    [Fact]
    public void Arbitration_RecordsFallbackAndChange_WhenLlmFlipsDecision()
    {
        // 加權投票會選 sell（3 票 sell vs 1 票 buy）；LLM 強制翻成 buy
        // → fallback_would_be=sell(-1)、changed_decision=1
        var override_ = new Signal
        {
            SignalId = "arb", Strategy = "ensemble", Symbol = "AAPL", Exchange = "alpaca",
            Action = "buy", Confidence = 0.85m, Reason = "LLM flips", Interval = "1d",
        };
        var stub = new StubArbitrator(0.8m, toReturn: override_);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "sell", 0.7m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.6m),
            new StubStrategy("d", "buy",  0.5m),
        }, arbitrator: stub);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        sig.Indicators["arbitration.invoked"].Should().Be(1m);
        sig.Indicators["arbitration.fallback_would_be"].Should().Be(-1m, "weighted vote was sell");
        sig.Indicators.Should().ContainKey("arbitration.fallback_confidence");
        sig.Indicators["arbitration.changed_decision"].Should().Be(1m, "LLM flipped sell→buy");
        sig.Indicators["arbitration.result_action"].Should().Be(1m);
    }

    [Fact]
    public void Arbitration_ChangedDecisionZero_WhenLlmAgreesWithVote()
    {
        // 加權投票會選 sell；LLM 也回 sell（沒翻轉）→ changed_decision=0
        var sameDir = new Signal
        {
            SignalId = "arb", Strategy = "ensemble", Symbol = "AAPL", Exchange = "alpaca",
            Action = "sell", Confidence = 0.9m, Reason = "LLM concurs", Interval = "1d",
        };
        var stub = new StubArbitrator(0.8m, toReturn: sameDir);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "sell", 0.7m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.6m),
            new StubStrategy("d", "buy",  0.5m),
        }, arbitrator: stub);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        sig.Indicators["arbitration.fallback_would_be"].Should().Be(-1m);
        sig.Indicators["arbitration.changed_decision"].Should().Be(0m, "LLM concurred with weighted vote");
    }

    [Fact]
    public void Arbitration_ChangedDecisionZero_WhenLlmReturnsNull()
    {
        // arbitrator 回 null（拒絕意見）→ fallback indicator 仍要寫入、changed=0
        var stub = new StubArbitrator(0.8m, toReturn: null);
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "sell", 0.7m),
            new StubStrategy("b", "sell", 0.7m),
            new StubStrategy("c", "sell", 0.6m),
            new StubStrategy("d", "buy",  0.5m),
        }, arbitrator: stub);

        var sig = ensemble.Evaluate(MakeBars(), Cfg());

        sig.Indicators["arbitration.fallback_would_be"].Should().Be(-1m);
        sig.Indicators["arbitration.changed_decision"].Should().Be(0m, "arbitrator declined");
    }
}
