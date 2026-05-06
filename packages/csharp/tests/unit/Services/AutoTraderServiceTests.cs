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

        var removed = svc.RemoveWatch("AAPL", "alpaca");

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
}
