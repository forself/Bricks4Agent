using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Unit.Tests.Helpers;

namespace Unit.Tests.Services;

/// <summary>
/// AlertRulesService CRUD + persistence 測試。
///
/// 主迴圈（EvaluateAllRulesAsync）需要真實 dispatcher + worker registry 才有意義，
/// 那部分依靠整合測試；這邊只測 CRUD 跟 mirror/SQLite 同步。
/// </summary>
public class AlertRulesServiceTests
{
    private static AlertRulesService MakeService(BrokerDb db)
    {
        var dispatcher = Substitute.For<IExecutionDispatcher>();
        var registry   = Substitute.For<IWorkerRegistry>();
        return new AlertRulesService(dispatcher, registry, db, NullLogger<AlertRulesService>.Instance);
    }

    [Fact]
    public void EmptyDb_StartsWithNoRules()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        svc.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Create_PersistsAndReturnsRuleWithId()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        var rule = svc.Create("BTC alert", "price_above", "BTCUSDT", "binance", 70_000m, cooldownMinutes: 15);

        rule.Id.Should().StartWith("rule-");
        rule.Symbol.Should().Be("BTCUSDT", "Create should normalize symbol to uppercase");
        rule.Exchange.Should().Be("binance");
        rule.Threshold.Should().Be(70_000m);
        rule.CooldownMinutes.Should().Be(15);
        rule.Enabled.Should().BeTrue();

        // 在記憶體 mirror 裡
        svc.Rules.Should().ContainKey(rule.Id);
        // 在 DB 裡
        var fromDb = db.Get<AlertRuleEntry>(rule.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Threshold.Should().Be(70_000m);
    }

    [Fact]
    public void Create_NormalizesSymbolAndExchangeCase()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        var rule = svc.Create("test", "price_above", "btcusdt", "BINANCE", 100m);

        rule.Symbol.Should().Be("BTCUSDT");
        rule.Exchange.Should().Be("binance");
    }

    [Fact]
    public void Update_MutatesRuleAndPersists()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);
        var rule = svc.Create("orig", "price_above", "AAPL", "alpaca", 200m);

        var ok = svc.Update(rule.Id, r => { r.Threshold = 250m; r.Enabled = false; });

        ok.Should().BeTrue();
        svc.Rules[rule.Id].Threshold.Should().Be(250m);
        svc.Rules[rule.Id].Enabled.Should().BeFalse();
        var fromDb = db.Get<AlertRuleEntry>(rule.Id);
        fromDb!.Threshold.Should().Be(250m);
        fromDb.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Update_OnUnknownId_ReturnsFalse()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        var ok = svc.Update("rule-nonexistent", _ => { });

        ok.Should().BeFalse();
    }

    [Fact]
    public void Delete_RemovesFromMirrorAndDb()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);
        var rule = svc.Create("delete me", "price_below", "TSLA", "alpaca", 100m);

        var ok = svc.Delete(rule.Id);

        ok.Should().BeTrue();
        svc.Rules.Should().NotContainKey(rule.Id);
        db.Get<AlertRuleEntry>(rule.Id).Should().BeNull();
    }

    [Fact]
    public void Delete_OnUnknownId_ReturnsFalse()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        var ok = svc.Delete("rule-nonexistent");

        ok.Should().BeFalse();
    }

    [Fact]
    public void Restart_LoadsRulesFromDb()
    {
        // 模擬 broker 重啟：第一個 service 寫入規則、第二個 service 從同一 DB 讀
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc1 = MakeService(db);
        svc1.Create("r1", "price_above", "AAPL", "alpaca", 200m);
        svc1.Create("r2", "price_below", "TSLA", "alpaca", 100m);

        var svc2 = MakeService(db);

        svc2.Rules.Should().HaveCount(2);
    }

    [Fact]
    public void GetEvents_DefaultLimit_ReturnsRecentFirst()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        // 直接 insert 三筆 event 進 DB（避開 polling）
        var t = new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 3; i++)
            db.Insert(new AlertEventEntry
            {
                Id = $"evt-{i:D2}", RuleId = "rule-x", RuleName = "x",
                ConditionType = "price_above", Symbol = "AAPL", Exchange = "alpaca",
                Threshold = 100m, ObservedValue = 110m + i, Message = $"msg{i}",
                TriggeredAt = t.AddMinutes(i),  // 越大越近
            });

        var events = svc.GetEvents(limit: 50);

        events.Should().HaveCount(3);
        events[0].Id.Should().Be("evt-02", "newest first");
        events[2].Id.Should().Be("evt-00");
    }

    [Fact]
    public void GetEvents_UnacknowledgedOnly_FiltersAcked()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        var t = new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc);
        db.Insert(new AlertEventEntry { Id = "evt-1", RuleId = "x", RuleName = "x", ConditionType = "price_above", Symbol = "AAPL", Exchange = "alpaca", Threshold = 100m, ObservedValue = 110m, TriggeredAt = t, AcknowledgedAt = null });
        db.Insert(new AlertEventEntry { Id = "evt-2", RuleId = "x", RuleName = "x", ConditionType = "price_above", Symbol = "AAPL", Exchange = "alpaca", Threshold = 100m, ObservedValue = 110m, TriggeredAt = t, AcknowledgedAt = t });

        svc.GetEvents(unacknowledgedOnly: true).Should().ContainSingle()
            .Which.Id.Should().Be("evt-1");
        svc.GetEvents(unacknowledgedOnly: false).Should().HaveCount(2);
    }

    [Fact]
    public void Acknowledge_SetsTimestamp()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        db.Insert(new AlertEventEntry
        {
            Id = "evt-ack-test", RuleId = "x", RuleName = "x",
            ConditionType = "price_above", Symbol = "AAPL", Exchange = "alpaca",
            Threshold = 100m, ObservedValue = 110m, TriggeredAt = DateTime.UtcNow,
        });

        svc.Acknowledge("evt-ack-test").Should().BeTrue();

        var evt = db.Get<AlertEventEntry>("evt-ack-test");
        evt.Should().NotBeNull();
        evt!.AcknowledgedAt.Should().NotBeNull();
    }

    [Fact]
    public void Acknowledge_OnUnknownId_ReturnsFalse()
    {
        using var db = TestDb.CreateInMemory();
        db.EnsureTable<AlertRuleEntry>(); db.EnsureTable<AlertEventEntry>();
        var svc = MakeService(db);

        svc.Acknowledge("evt-nonexistent").Should().BeFalse();
    }
}
