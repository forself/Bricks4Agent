using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Unit.Tests.Helpers;

namespace Unit.Tests.Services;

/// <summary>
/// AutoTraderService E2E coverage（Cut 3）：cuts 1 (watchlist persistence) +
/// cut 2a (order idempotency / deterministic key) 的回歸網。
///
/// Cut 2b 的 fill polling 在 trading-worker，這個 test project 沒 reference 那邊，
/// 所以這裡只覆蓋 broker 端的合約。FillPoller 自己的單測未來再加一個 trading-worker-tests
/// 子 project 處理。
/// </summary>
public class AutoTraderServiceTests
{
    private static AutoTraderService MakeService(BrokerDb db)
    {
        var dispatcher = Substitute.For<IExecutionDispatcher>();
        var registry = Substitute.For<IWorkerRegistry>();
        return new AutoTraderService(dispatcher, registry, db, NullLogger<AutoTraderService>.Instance);
    }

    // ── BuildAutoOrderKey（純函式，cut 2a 的核心邏輯）─────────────────

    [Fact]
    public void BuildAutoOrderKey_IsDeterministic_ForSameInputs()
    {
        var k1 = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "buy", 1m, 9123456L);
        var k2 = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "buy", 1m, 9123456L);
        k1.Should().Be(k2);
    }

    [Fact]
    public void BuildAutoOrderKey_DiffersAcrossBuckets()
    {
        var k1 = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "buy", 1m, 9123456L);
        var k2 = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "buy", 1m, 9123457L);
        k1.Should().NotBe(k2);
    }

    [Fact]
    public void BuildAutoOrderKey_DiffersAcrossSides()
    {
        var buy  = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "buy",  1m, 9123456L);
        var sell = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "sell", 1m, 9123456L);
        buy.Should().NotBe(sell);
    }

    [Fact]
    public void BuildAutoOrderKey_ReplacesDotInDecimalQuantity_ForBinanceCompat()
    {
        // Binance newClientOrderId 限 [a-zA-Z0-9-_] —— 不能有 dot
        var key = AutoTraderService.BuildAutoOrderKey("binance", "BTCUSDT", "buy", 0.5m, 9123456L);
        key.Should().NotContain(".");
        key.Should().Contain("0_5");
    }

    [Fact]
    public void BuildAutoOrderKey_TruncatesTo36Chars_ForBinanceLimit()
    {
        // Symbol 故意給長一點，確保截斷邏輯生效
        var key = AutoTraderService.BuildAutoOrderKey("binance", "VERY-LONG-PAIR-NAME-SYM", "buy", 12345.6789m, 9123456L);
        key.Length.Should().BeLessThanOrEqualTo(36);
    }

    [Fact]
    public void BuildAutoOrderKey_StartsWithAutoPrefix_ForGrepability()
    {
        // 在 dashboard / log / DB 裡能 grep "auto-" 知道是 auto-trader 出的單
        var key = AutoTraderService.BuildAutoOrderKey("alpaca", "AAPL", "buy", 1m, 9123456L);
        key.Should().StartWith("auto-");
    }

    // ── Watchlist 持久化（cut 1 的核心契約）─────────────────────────

    [Fact]
    public void AddWatch_PersistsRowToDb()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        svc.AddWatch("AAPL", "alpaca", "composite", quantity: 2m);

        var rows = db.GetAll<AutoTradeWatchEntry>();
        rows.Should().ContainSingle();
        rows[0].Symbol.Should().Be("AAPL");
        rows[0].Exchange.Should().Be("alpaca");
        rows[0].Strategy.Should().Be("composite");
        rows[0].Quantity.Should().Be(2m);
        rows[0].Active.Should().BeTrue();
    }

    [Fact]
    public void RemoveWatch_DeletesPersistedRow()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);
        svc.AddWatch("AAPL", "alpaca");

        var (removed, _) = svc.RemoveWatch("AAPL", "alpaca");

        removed.Should().BeTrue();
        db.GetAll<AutoTradeWatchEntry>().Should().BeEmpty();
        svc.WatchList.Should().BeEmpty();
    }

    [Fact]
    public void PauseWatch_PersistsActiveFalse()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);
        svc.AddWatch("AAPL", "alpaca");

        svc.PauseWatch("AAPL", "alpaca");

        db.Get<AutoTradeWatchEntry>("alpaca:AAPL")!.Active.Should().BeFalse();
    }

    [Fact]
    public void ResumeWatch_PersistsActiveTrue()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);
        svc.AddWatch("AAPL", "alpaca");
        svc.PauseWatch("AAPL", "alpaca");

        svc.ResumeWatch("AAPL", "alpaca");

        db.Get<AutoTradeWatchEntry>("alpaca:AAPL")!.Active.Should().BeTrue();
    }

    [Fact]
    public void Constructor_LoadsExistingWatchlistFromDb()
    {
        // 模擬 broker 重啟 scenario：先寫一筆 entry 到 DB，再用一個全新 service 把它 load 回來
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();
        var now = DateTime.UtcNow;
        db.Insert(new AutoTradeWatchEntry
        {
            EntryKey = "alpaca:TSLA", Symbol = "TSLA", Exchange = "alpaca",
            Strategy = "rsi_oversold", Quantity = 3m, Active = true,
            LastSignal = "buy", LastConfidence = 0.75m, LastCheck = now,
            CreatedAt = now, UpdatedAt = now,
        });

        var svc = MakeService(db);

        svc.WatchList.Should().ContainKey("alpaca:TSLA");
        var item = svc.WatchList["alpaca:TSLA"];
        item.Symbol.Should().Be("TSLA");
        item.Strategy.Should().Be("rsi_oversold");
        item.Quantity.Should().Be(3m);
        item.LastSignal.Should().Be("buy");
        item.LastConfidence.Should().Be(0.75m);
    }

    // ── Dev-only force action env override ────────────────────────

    [Fact]
    public void DevForceAction_DefaultEnvUnset_IsNull()
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_DEV_FORCE_ACTION", null);
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();

        var svc = MakeService(db);

        svc.DevForceAction.Should().BeNull();
    }

    [Theory]
    [InlineData("buy")]
    [InlineData("sell")]
    [InlineData("BUY")]   // case-insensitive
    [InlineData("Sell")]
    public void DevForceAction_EnvSetToValidAction_ParsedAndExposed(string raw)
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_DEV_FORCE_ACTION", raw);
        try
        {
            using var db = TestDb.CreateInMemory();
            db.EnsureTable<AutoTradeWatchEntry>();
            var svc = MakeService(db);
            svc.DevForceAction.Should().Be(raw.Trim().ToLowerInvariant());
        }
        finally { Environment.SetEnvironmentVariable("AUTOTRADER_DEV_FORCE_ACTION", null); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hold")]
    [InlineData("invalid")]
    [InlineData("BUYSELL")]
    public void DevForceAction_EnvSetToInvalidValue_StaysNull(string raw)
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_DEV_FORCE_ACTION", raw);
        try
        {
            using var db = TestDb.CreateInMemory();
            db.EnsureTable<AutoTradeWatchEntry>();
            var svc = MakeService(db);
            svc.DevForceAction.Should().BeNull("only buy/sell should activate the override");
        }
        finally { Environment.SetEnvironmentVariable("AUTOTRADER_DEV_FORCE_ACTION", null); }
    }

    // ── AUTOTRADER_MIN_CONFIDENCE env override ────────────────────

    [Fact]
    public void MinConfidence_DefaultEnvUnset_Is_0_5()
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", null);
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();

        var svc = MakeService(db);

        svc.MinConfidence.Should().Be(0.5m);
    }

    [Theory]
    [InlineData("0.45", 0.45)]
    [InlineData("0.6",  0.6)]
    [InlineData("0",    0)]
    [InlineData("1",    1)]
    public void MinConfidence_EnvSetToValidValue_IsParsed(string raw, decimal expected)
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", raw);
        try
        {
            using var db = TestDb.CreateInMemory();
            db.EnsureTable<AutoTradeWatchEntry>();
            var svc = MakeService(db);
            svc.MinConfidence.Should().Be(expected);
        }
        finally { Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", null); }
    }

    [Theory]
    [InlineData("-0.1", 0)]   // negative → clamp 0
    [InlineData("1.5",  1)]   // > 1 → clamp 1
    [InlineData("999",  1)]
    public void MinConfidence_EnvOutOfRange_IsClamped(string raw, decimal expected)
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", raw);
        try
        {
            using var db = TestDb.CreateInMemory();
            db.EnsureTable<AutoTradeWatchEntry>();
            var svc = MakeService(db);
            svc.MinConfidence.Should().Be(expected);
        }
        finally { Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", null); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanumber")]
    [InlineData("0.5x")]
    public void MinConfidence_EnvSetToGarbage_FallsBackToDefault(string raw)
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", raw);
        try
        {
            using var db = TestDb.CreateInMemory();
            db.EnsureTable<AutoTradeWatchEntry>();
            var svc = MakeService(db);
            svc.MinConfidence.Should().Be(0.5m, "garbage env value should fall back to default 0.5");
        }
        finally { Environment.SetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE", null); }
    }

    [Fact]
    public void ParseMinConfidence_DirectStaticHelper_HandlesEdgeCases()
    {
        // 直接驗 static helper、不繞 env
        AutoTraderService.ParseMinConfidence(null).Should().Be(0.5m);
        AutoTraderService.ParseMinConfidence("").Should().Be(0.5m);
        AutoTraderService.ParseMinConfidence("0.3").Should().Be(0.3m);
        AutoTraderService.ParseMinConfidence("0").Should().Be(0m);
        AutoTraderService.ParseMinConfidence("1").Should().Be(1m);
        AutoTraderService.ParseMinConfidence("-5").Should().Be(0m);
        AutoTraderService.ParseMinConfidence("2").Should().Be(1m);
    }

    // ── B1 Portfolio circuit breaker ──────────────────────────────

    [Fact]
    public void MaxPortfolioDdPct_DefaultEnvUnset_Is_8()
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_MAX_PORTFOLIO_DD_PCT", null);
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        svc.MaxPortfolioDdPct.Should().Be(8m);
    }

    [Theory]
    [InlineData("3",  3)]
    [InlineData("12.5", 12.5)]
    [InlineData("100", 100)]
    public void MaxPortfolioDdPct_EnvSetToValidValue_IsParsed(string raw, decimal expected)
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_MAX_PORTFOLIO_DD_PCT", raw);
        try
        {
            using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
            var svc = MakeService(db);
            svc.MaxPortfolioDdPct.Should().Be(expected);
        }
        finally { Environment.SetEnvironmentVariable("AUTOTRADER_MAX_PORTFOLIO_DD_PCT", null); }
    }

    [Theory]
    [InlineData("", 8)]            // empty → default
    [InlineData("garbage", 8)]
    [InlineData("0", 8)]           // 0 視為無效（會永遠觸發）→ 走預設
    [InlineData("-5", 8)]
    [InlineData("150", 100)]       // > 100 clamp 到 100
    public void ParseMaxPortfolioDdPct_EdgeCases(string raw, decimal expected)
    {
        AutoTraderService.ParseMaxPortfolioDdPct(raw).Should().Be(expected);
    }

    [Fact]
    public void EvaluateCircuitBreaker_FirstCallSetsPeak_ReturnsNotTriggered()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var now = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var r = svc.EvaluateCircuitBreaker("alpaca", 100_000m, now);

        r.Triggered.Should().BeFalse();
        r.PeakValue.Should().Be(100_000m);
        r.DdPct.Should().Be(0m);
    }

    [Fact]
    public void EvaluateCircuitBreaker_PortfolioRises_PeakRaisedNotTriggered()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.EvaluateCircuitBreaker("alpaca", 100_000m, t0);
        var r = svc.EvaluateCircuitBreaker("alpaca", 105_000m, t0.AddMinutes(5));

        r.Triggered.Should().BeFalse();
        r.PeakValue.Should().Be(105_000m, "rising portfolio raises peak");
        r.DdPct.Should().Be(0m);
    }

    [Fact]
    public void EvaluateCircuitBreaker_DdBelowThreshold_NotTriggered()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);  // default 8%

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.EvaluateCircuitBreaker("alpaca", 100_000m, t0);
        var r = svc.EvaluateCircuitBreaker("alpaca", 95_000m, t0.AddMinutes(5));  // 5% DD

        r.Triggered.Should().BeFalse();
        r.DdPct.Should().Be(5m);
        r.Threshold.Should().Be(8m);
    }

    [Fact]
    public void EvaluateCircuitBreaker_DdAtOrAboveThreshold_Triggered()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);  // default 8%

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.EvaluateCircuitBreaker("alpaca", 100_000m, t0);
        var r = svc.EvaluateCircuitBreaker("alpaca", 92_000m, t0.AddMinutes(5));  // 8% DD

        r.Triggered.Should().BeTrue("DD ≥ threshold should trigger");
        r.DdPct.Should().Be(8m);
        r.PeakValue.Should().Be(100_000m);
    }

    [Fact]
    public void EvaluateCircuitBreaker_NextDayResetsPeak()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        // Day 1: 100k peak, drops to 92k → triggered
        var d1 = new DateTime(2026, 5, 6, 23, 30, 0, DateTimeKind.Utc);
        svc.EvaluateCircuitBreaker("alpaca", 100_000m, d1);
        var d1End = svc.EvaluateCircuitBreaker("alpaca", 92_000m, d1.AddMinutes(15));
        d1End.Triggered.Should().BeTrue();

        // Day 2: same 92k value, now is the new peak → not triggered
        var d2 = new DateTime(2026, 5, 7, 1, 0, 0, DateTimeKind.Utc);
        var d2Start = svc.EvaluateCircuitBreaker("alpaca", 92_000m, d2);
        d2Start.Triggered.Should().BeFalse("UTC midnight should reset peak");
        d2Start.PeakValue.Should().Be(92_000m);
        d2Start.DdPct.Should().Be(0m);
    }

    [Fact]
    public void EvaluateCircuitBreaker_PerExchangeIndependentTracking()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.EvaluateCircuitBreaker("alpaca",  100_000m, t0);
        svc.EvaluateCircuitBreaker("binance", 50_000m,  t0);

        // Alpaca crash 8%, Binance flat
        var ar = svc.EvaluateCircuitBreaker("alpaca",  92_000m, t0.AddMinutes(5));
        var br = svc.EvaluateCircuitBreaker("binance", 50_000m, t0.AddMinutes(5));

        ar.Triggered.Should().BeTrue();
        br.Triggered.Should().BeFalse("Binance peak/DD tracked separately");
        br.PeakValue.Should().Be(50_000m);
    }

    [Fact]
    public void EvaluateCircuitBreaker_ZeroOrNegativeCurrentValue_NotTriggered()
    {
        // worker 連不上、acc 失敗 → portfolio_value=0；不該誤觸 circuit breaker
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var r = svc.EvaluateCircuitBreaker("alpaca", 0m, DateTime.UtcNow);

        r.Triggered.Should().BeFalse();
        r.DdPct.Should().Be(0m);
    }

    [Fact]
    public void CircuitBreakerSnapshot_ReflectsLastEvaluation()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.EvaluateCircuitBreaker("alpaca", 100_000m, t0);
        svc.EvaluateCircuitBreaker("alpaca", 92_000m, t0.AddMinutes(5));

        svc.CircuitBreakerSnapshot.Should().ContainKey("alpaca");
        // snapshot 內容透過反射檢查不容易、用 JSON serialize 後 grep 關鍵字確認結構即可
        var json = System.Text.Json.JsonSerializer.Serialize(svc.CircuitBreakerSnapshot);
        json.Should().Contain("peak_value");
        json.Should().Contain("dd_pct");
        json.Should().Contain("triggered");
    }

    // ── B4a Position protection (pure decision) ───────────────────

    private static AutoTraderService.ProtectionConfig DefaultProtConfig() => new()
    {
        InitialSlPct        = 5m,
        PartialExitPct      = 5m,
        PartialExitRatio    = 0.5m,
        BreakevenTriggerPct = 3m,
        BreakevenBufferPct  = 0.5m,
    };

    private static AutoTraderService.PositionProtectionState MakeState(
        decimal entry = 100m, decimal? sl = null, bool partialExited = false, bool beMoved = false, decimal? peak = null) => new()
    {
        Exchange = "alpaca", Symbol = "AAPL",
        EntryPrice = entry, PeakPrice = peak ?? entry,
        SlPrice = sl ?? entry * 0.95m,
        PartialExited = partialExited, BeMoved = beMoved,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void Protection_PriceAtSL_TriggersSlHit()
    {
        // Entry 100, SL 95；當前 95 → SL hit
        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m, sl: 95m), 95m, 10m, DefaultProtConfig());

        d.Action.Should().Be(AutoTraderService.ProtectionAction.SlHit);
        d.PartialQty.Should().Be(10m);
        d.Reason.Should().Contain("SL hit");
    }

    [Fact]
    public void Protection_PriceBelowSL_TriggersSlHit()
    {
        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m, sl: 95m), 90m, 10m, DefaultProtConfig());
        d.Action.Should().Be(AutoTraderService.ProtectionAction.SlHit);
    }

    [Fact]
    public void Protection_PnlAtPartialExitThreshold_TriggersPartialExit()
    {
        // Entry 100, +5% = 105 → partial exit
        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m), 105m, 10m, DefaultProtConfig());

        d.Action.Should().Be(AutoTraderService.ProtectionAction.PartialExit);
        d.PartialQty.Should().Be(5m, "10 × 0.5 ratio = 5");
    }

    [Fact]
    public void Protection_AfterPartialExit_DoesNotRePartialExit()
    {
        // Already exited, +10% — should not trigger partial again
        var state = MakeState(entry: 100m, partialExited: true);
        var d = AutoTraderService.EvaluateProtection(state, 110m, 5m, DefaultProtConfig());

        d.Action.Should().NotBe(AutoTraderService.ProtectionAction.PartialExit);
    }

    [Fact]
    public void Protection_PnlAtBreakevenThreshold_TriggersBeMove()
    {
        // Entry 100, +3% = 103，BE trigger=3% → SL 移到 entry × 1.005 = 100.5
        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m, sl: 95m), 103m, 10m, DefaultProtConfig());

        // 注意：3% 也滿足 partial exit 條件嗎？partial=5%、3<5 所以這裡 partial 不會觸發、走 BE
        d.Action.Should().Be(AutoTraderService.ProtectionAction.BeMove);
        d.NewSlPrice.Should().Be(100.5m, "entry 100 × (1 + 0.5%) = 100.5");
    }

    [Fact]
    public void Protection_AfterBeMoveDone_DoesNotReTrigger()
    {
        var state = MakeState(entry: 100m, sl: 100.5m, beMoved: true);
        var d = AutoTraderService.EvaluateProtection(state, 103m, 10m, DefaultProtConfig());

        d.Action.Should().NotBe(AutoTraderService.ProtectionAction.BeMove);
    }

    [Fact]
    public void Protection_SlHasPriorityOverPartialExit()
    {
        // 若 SL 跟 partial 同時都該觸發、SL 優先（因為已虧損、優先止損）
        // 雖然這 case 物理上少見（當前價 < SL 但 PnL > +5% 不可能），但邏輯保險還是先 SL。
        // 實際模擬：entry=100, sl=110（已 BE 後挪上去），current=109（破 sl）
        var state = MakeState(entry: 100m, sl: 110m, beMoved: true);
        var d = AutoTraderService.EvaluateProtection(state, 109m, 10m, DefaultProtConfig());

        d.Action.Should().Be(AutoTraderService.ProtectionAction.SlHit, "BE 後挪上的 SL 被打到 = 鎖小利、優先觸發");
    }

    [Fact]
    public void Protection_BetweenThresholds_ReturnsNone()
    {
        // +1.5% 在 BE trigger (3%) 之下 → 沒事
        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m), 101.5m, 10m, DefaultProtConfig());

        d.Action.Should().Be(AutoTraderService.ProtectionAction.None);
        d.PnlPct.Should().Be(1.5m);
    }

    [Fact]
    public void Protection_InvalidInputs_ReturnsNone()
    {
        var cfg = DefaultProtConfig();
        AutoTraderService.EvaluateProtection(MakeState(entry: 0m), 100m, 10m, cfg).Action.Should().Be(AutoTraderService.ProtectionAction.None);
        AutoTraderService.EvaluateProtection(MakeState(), 0m, 10m, cfg).Action.Should().Be(AutoTraderService.ProtectionAction.None);
        AutoTraderService.EvaluateProtection(MakeState(), 100m, 0m, cfg).Action.Should().Be(AutoTraderService.ProtectionAction.None);
    }

    [Fact]
    public void Protection_PartialExitFloorsToZero_PreventsRoundUp()
    {
        // qty=0.001、ratio=0.5 → 0.0005 round 到小數 4 位 = 0.0005，但 ToZero 模式下保留 0
        // 確認小量倉位 partial 時不會因 round-up 賣超過實際 qty
        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m), 105m, 0.001m, DefaultProtConfig());

        // partialQty = round(0.001 * 0.5, 4, ToZero) = 0.0005
        // 0.0005 > 0 且 < 0.001 → partial exit OK
        d.Action.Should().Be(AutoTraderService.ProtectionAction.PartialExit);
        d.PartialQty.Should().Be(0.0005m);
    }

    [Fact]
    public void Protection_HighRatio_StillLeavesQty()
    {
        // ratio = 0.9：qty 5、賣 0.9 → 4.5 留 0.5
        var customCfg = new AutoTraderService.ProtectionConfig
        {
            InitialSlPct = 5m, PartialExitPct = 5m, PartialExitRatio = 0.9m,
            BreakevenTriggerPct = 3m, BreakevenBufferPct = 0.5m,
        };

        var d = AutoTraderService.EvaluateProtection(MakeState(entry: 100m), 105m, 5m, customCfg);

        d.Action.Should().Be(AutoTraderService.ProtectionAction.PartialExit);
        d.PartialQty.Should().Be(4.5m);
    }

    // ── B4a env parsing ───────────────────────────────────────────

    [Fact]
    public void ParsePctEnv_DefaultWhenUnset()
    {
        Environment.SetEnvironmentVariable("TEST_PCT_ENV", null);
        AutoTraderService.ParsePctEnv("TEST_PCT_ENV", 5m, 0.5m, 50m).Should().Be(5m);
    }

    [Theory]
    [InlineData("3.5", 3.5)]
    [InlineData("0.5", 0.5)]
    [InlineData("50",  50)]
    public void ParsePctEnv_ValidValueParsed(string raw, decimal expected)
    {
        Environment.SetEnvironmentVariable("TEST_PCT_ENV", raw);
        try { AutoTraderService.ParsePctEnv("TEST_PCT_ENV", 5m, 0.5m, 50m).Should().Be(expected); }
        finally { Environment.SetEnvironmentVariable("TEST_PCT_ENV", null); }
    }

    [Fact]
    public void ParsePctEnv_BelowMinFallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("TEST_PCT_ENV", "0.1");
        try { AutoTraderService.ParsePctEnv("TEST_PCT_ENV", 5m, 0.5m, 50m).Should().Be(5m); }
        finally { Environment.SetEnvironmentVariable("TEST_PCT_ENV", null); }
    }

    [Fact]
    public void ParsePctEnv_AboveMaxClamps()
    {
        Environment.SetEnvironmentVariable("TEST_PCT_ENV", "999");
        try { AutoTraderService.ParsePctEnv("TEST_PCT_ENV", 5m, 0.5m, 50m).Should().Be(50m); }
        finally { Environment.SetEnvironmentVariable("TEST_PCT_ENV", null); }
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("0.3", 0.3)]
    [InlineData("0.95", 0.95)]
    public void ParseRatioEnv_ValidValuesInOpenInterval(string raw, decimal expected)
    {
        Environment.SetEnvironmentVariable("TEST_RATIO_ENV", raw);
        try { AutoTraderService.ParseRatioEnv("TEST_RATIO_ENV", 0.5m).Should().Be(expected); }
        finally { Environment.SetEnvironmentVariable("TEST_RATIO_ENV", null); }
    }

    [Theory]
    [InlineData("0")]    // boundary not in (0, 1)
    [InlineData("1")]
    [InlineData("-0.5")]
    [InlineData("1.5")]
    [InlineData("garbage")]
    public void ParseRatioEnv_OutOfRangeOrGarbage_FallsBackToDefault(string raw)
    {
        Environment.SetEnvironmentVariable("TEST_RATIO_ENV", raw);
        try { AutoTraderService.ParseRatioEnv("TEST_RATIO_ENV", 0.7m).Should().Be(0.7m); }
        finally { Environment.SetEnvironmentVariable("TEST_RATIO_ENV", null); }
    }

    // ── B3 SL flush freeze ────────────────────────────────────────

    [Fact]
    public void ParseIntEnv_DefaultWhenUnsetOrGarbage()
    {
        Environment.SetEnvironmentVariable("TEST_INT_ENV", null);
        AutoTraderService.ParseIntEnv("TEST_INT_ENV", 5, 1, 100).Should().Be(5);

        Environment.SetEnvironmentVariable("TEST_INT_ENV", "abc");
        try { AutoTraderService.ParseIntEnv("TEST_INT_ENV", 5, 1, 100).Should().Be(5); }
        finally { Environment.SetEnvironmentVariable("TEST_INT_ENV", null); }
    }

    [Fact]
    public void ParseIntEnv_BelowMinFallsBackAboveMaxClamps()
    {
        Environment.SetEnvironmentVariable("TEST_INT_ENV", "0");
        try { AutoTraderService.ParseIntEnv("TEST_INT_ENV", 5, 1, 100).Should().Be(5); }
        finally { Environment.SetEnvironmentVariable("TEST_INT_ENV", null); }

        Environment.SetEnvironmentVariable("TEST_INT_ENV", "999");
        try { AutoTraderService.ParseIntEnv("TEST_INT_ENV", 5, 1, 100).Should().Be(100); }
        finally { Environment.SetEnvironmentVariable("TEST_INT_ENV", null); }
    }

    [Fact]
    public void SlFlush_DefaultsAre_3_And_60()
    {
        Environment.SetEnvironmentVariable("AUTOTRADER_SL_FLUSH_THRESHOLD", null);
        Environment.SetEnvironmentVariable("AUTOTRADER_SL_FLUSH_WINDOW_MINUTES", null);
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        svc.SlFlushThreshold.Should().Be(3);
        svc.SlFlushWindowMinutes.Should().Be(60);
    }

    [Fact]
    public void SlFlush_BelowThreshold_DoesNotTrigger()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);  // 預設 3 次 / 60 分

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.RecordSlHit("alpaca", "AAPL", t0);
        svc.RecordSlHit("alpaca", "TSLA", t0.AddMinutes(5));

        svc.SlFlushTriggered.Should().BeFalse();
        svc.RecentSlHits.Should().HaveCount(2);
    }

    [Fact]
    public void SlFlush_AtThreshold_TriggersAndDisablesAutoTrader()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);
        svc.Enable();
        svc.IsEnabled.Should().BeTrue();

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.RecordSlHit("alpaca", "AAPL", t0);
        svc.RecordSlHit("alpaca", "TSLA", t0.AddMinutes(5));
        svc.RecordSlHit("alpaca", "NVDA", t0.AddMinutes(10));

        svc.SlFlushTriggered.Should().BeTrue();
        svc.SlFlushTriggeredAt.Should().Be(t0.AddMinutes(10));
        svc.IsEnabled.Should().BeFalse("hitting flush threshold should auto-disable");
    }

    [Fact]
    public void SlFlush_OldHitsOutsideWindow_ArePruned()
    {
        // 預設 60 分鐘視窗：兩個 90 分鐘前的 hit 應該被移除、新 hit 不會湊到 3
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var t0 = new DateTime(2026, 5, 6, 10, 0, 0, DateTimeKind.Utc);
        svc.RecordSlHit("alpaca", "OLD1", t0);
        svc.RecordSlHit("alpaca", "OLD2", t0.AddMinutes(5));
        // 90 分後一個新 hit
        svc.RecordSlHit("alpaca", "NEW1", t0.AddMinutes(90));

        // 老的兩個應該被視窗外踢掉、剩下 1 個
        svc.SlFlushTriggered.Should().BeFalse();
        svc.RecentSlHits.Should().HaveCount(1);
        svc.RecentSlHits[0].Symbol.Should().Be("NEW1");
    }

    [Fact]
    public void SlFlush_AlreadyTriggered_DoesNotDoubleTrigger()
    {
        // 觸發後再加 SL，不應該重設 _slFlushTriggeredAt（保留首次觸發時間）
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.RecordSlHit("alpaca", "A", t0);
        svc.RecordSlHit("alpaca", "B", t0.AddMinutes(5));
        svc.RecordSlHit("alpaca", "C", t0.AddMinutes(10));   // 觸發
        var firstTrigger = svc.SlFlushTriggeredAt;

        svc.RecordSlHit("alpaca", "D", t0.AddMinutes(15));   // 多一個
        svc.SlFlushTriggeredAt.Should().Be(firstTrigger, "first trigger time should be sticky");
    }

    [Fact]
    public void SlFlush_Reset_ClearsQueueAndFlag()
    {
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.RecordSlHit("alpaca", "A", t0);
        svc.RecordSlHit("alpaca", "B", t0.AddMinutes(5));
        svc.RecordSlHit("alpaca", "C", t0.AddMinutes(10));
        svc.SlFlushTriggered.Should().BeTrue();

        svc.ResetSlFlush();

        svc.SlFlushTriggered.Should().BeFalse();
        svc.SlFlushTriggeredAt.Should().BeNull();
        svc.RecentSlHits.Should().BeEmpty();
        // 注意：reset 不自動 re-enable auto-trader——user 要手動按 enable
        svc.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SlFlush_PerExchangeEventsAllCount()
    {
        // 跨 exchange 的 SL hit 也算進同一個 flush 計數（這是「全帳戶 backstop」）
        using var db = TestDb.CreateInMemory(); db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);
        svc.Enable();

        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        svc.RecordSlHit("alpaca",  "AAPL",    t0);
        svc.RecordSlHit("binance", "BTCUSDT", t0.AddMinutes(5));
        svc.RecordSlHit("alpaca",  "TSLA",    t0.AddMinutes(10));

        svc.SlFlushTriggered.Should().BeTrue();
        svc.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AddSameKeyTwice_OverwritesAndKeepsSingleRow()
    {
        // 設計上同 (exchange, symbol) 任何時候只能有一筆 watch；重覆 AddWatch 應該覆寫不複製
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AutoTradeWatchEntry>();
        var svc = MakeService(db);

        svc.AddWatch("AAPL", "alpaca", "composite", 1m);
        svc.AddWatch("AAPL", "alpaca", "rsi_oversold", 5m);

        var rows = db.GetAll<AutoTradeWatchEntry>();
        rows.Should().ContainSingle();
        rows[0].Strategy.Should().Be("rsi_oversold");
        rows[0].Quantity.Should().Be(5m);
    }

    // ── Phase 4: Perpetual protection (pure decision) ─────────────

    private static AutoTraderService.PerpetualPositionState MakePerpState(
        string side = "long", decimal entry = 100m, decimal? sl = null,
        bool partialExited = false, bool beMoved = false, decimal liqPrice = 0m) => new()
    {
        Exchange = "bingx", Symbol = "BTC-USDT", Side = side,
        EntryPrice = entry, PeakMark = entry,
        SlPrice = sl ?? (side == "long" ? entry * 0.95m : entry * 1.05m),
        LiquidationPrice = liqPrice,
        Leverage = 5,
        PartialExited = partialExited, BeMoved = beMoved,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void PerpProtection_LongPriceAtSL_TriggersSlHit()
    {
        // Long entry 100, SL 95；mark 95 → SL hit (mark ≤ sl)
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("long", entry: 100m, sl: 95m), 95m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.SlHit);
        d.PartialQty.Should().Be(10m);
        d.Reason.Should().Contain("SL hit (long)");
    }

    [Fact]
    public void PerpProtection_ShortPriceAtSL_TriggersSlHit()
    {
        // Short entry 100, SL 105；mark 105 → SL hit (mark ≥ sl 才是反向)
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("short", entry: 100m, sl: 105m), 105m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.SlHit);
        d.Reason.Should().Contain("SL hit (short)");
    }

    [Fact]
    public void PerpProtection_LongPnlAtPartialThreshold_TriggersPartial()
    {
        // Long entry 100, mark 105 → +5% pnl → partial exit
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("long", entry: 100m), 105m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.PartialExit);
        d.PartialQty.Should().Be(5m);
    }

    [Fact]
    public void PerpProtection_ShortPnlAtPartialThreshold_TriggersPartial()
    {
        // Short entry 100, mark 95 → +5% pnl (entry-mark 算)
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("short", entry: 100m), 95m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.PartialExit);
        d.PartialQty.Should().Be(5m);
    }

    [Fact]
    public void PerpProtection_LongBeMove_NewSlAboveEntry()
    {
        // Long entry 100, mark 103 → +3% pnl → BE move with 0.5% buffer = 100.5
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("long", entry: 100m, sl: 95m), 103m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.BeMove);
        d.NewSlPrice.Should().Be(100.5m, "long BE: entry × (1 + buffer/100)");
    }

    [Fact]
    public void PerpProtection_ShortBeMove_NewSlBelowEntry()
    {
        // Short entry 100, mark 97 → +3% pnl → BE move with 0.5% buffer = 99.5
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("short", entry: 100m, sl: 105m), 97m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.BeMove);
        d.NewSlPrice.Should().Be(99.5m, "short BE: entry × (1 - buffer/100)");
    }

    [Fact]
    public void PerpProtection_LiquidationEmergency_TopPriority()
    {
        // 距強平 3%（< 5% 預設）→ emergency close、即使 SL 還沒到、partial / BE 也不重要
        var state = MakePerpState("long", entry: 100m, sl: 95m, liqPrice: 92m);
        state.PeakMark = 110m;  // 已賺很多、partial 跟 BE 應該都會 trigger，但 liq 優先
        var d = AutoTraderService.EvaluatePerpetualProtection(
            state, 110m, 10m, liqDistancePct: 3m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.LiquidationEmergency);
        d.PartialQty.Should().Be(10m, "emergency closes full position");
        d.Reason.Should().Contain("liquidation emergency");
    }

    [Fact]
    public void PerpProtection_LiquidationFar_NoEmergency()
    {
        // 距強平 20%（> 5%）→ 不觸發
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("long", entry: 100m, sl: 95m, liqPrice: 80m), 100m, 10m, 20m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().NotBe(AutoTraderService.PerpProtectionAction.LiquidationEmergency);
    }

    [Fact]
    public void PerpProtection_AfterBeAndShortFalls_SlHitOnNewSl()
    {
        // Short entry 100、BE 後 SL 移到 99.5、mark 突破 99.5 → SL hit (short 反向：mark ≥ sl)
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("short", entry: 100m, sl: 99.5m, beMoved: true), 99.5m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.SlHit);
    }

    [Fact]
    public void PerpProtection_AfterPartialExited_DoesNotRePartial()
    {
        var state = MakePerpState("long", entry: 100m, partialExited: true);
        var d = AutoTraderService.EvaluatePerpetualProtection(state, 110m, 5m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().NotBe(AutoTraderService.PerpProtectionAction.PartialExit);
    }

    [Fact]
    public void PerpProtection_BetweenThresholds_ReturnsNone()
    {
        // Long entry 100, mark 101.5 → +1.5% pnl，partial 5%、BE 3% 都沒到
        var d = AutoTraderService.EvaluatePerpetualProtection(
            MakePerpState("long", entry: 100m), 101.5m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.None);
        d.PnlPct.Should().Be(1.5m);
    }

    [Fact]
    public void PerpProtection_InvalidInputs_ReturnsNone()
    {
        var cfg = DefaultProtConfig();
        AutoTraderService.EvaluatePerpetualProtection(MakePerpState(entry: 0m), 100m, 10m, 50m, cfg, 5m).Action.Should().Be(AutoTraderService.PerpProtectionAction.None);
        AutoTraderService.EvaluatePerpetualProtection(MakePerpState(), 0m, 10m, 50m, cfg, 5m).Action.Should().Be(AutoTraderService.PerpProtectionAction.None);
        AutoTraderService.EvaluatePerpetualProtection(MakePerpState(), 100m, 0m, 50m, cfg, 5m).Action.Should().Be(AutoTraderService.PerpProtectionAction.None);
    }

    [Fact]
    public void PerpProtection_SlHitTakesPriorityOverPartial()
    {
        // Long entry 100, mark 95（SL hit + partial 都 trigger 不到）— 主要驗 SL 先觸發
        // 用 SL 100 已經 BE 後挪上來的 case：mark 100 = SL hit (≤ sl)
        var state = MakePerpState("long", entry: 90m, sl: 100m, beMoved: true);
        // mark 100 = SL hit, but pnl = (100-90)/90 = 11.1% 滿足 partial 條件
        var d = AutoTraderService.EvaluatePerpetualProtection(state, 100m, 10m, 50m, DefaultProtConfig(), liqEmergencyPct: 5m);

        d.Action.Should().Be(AutoTraderService.PerpProtectionAction.SlHit, "SL hit should win when both apply");
    }
}
