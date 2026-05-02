using TradingWorker.Models;
using TradingWorker.Tests.Helpers;

namespace TradingWorker.Tests.Storage;

/// <summary>
/// 鎖住 cut 2b 加的兩個查詢方法的契約：
///   - GetOpenOrders 只回非 terminal 狀態的單（給 fill poller 拿）
///   - GetDailyTradeCount 從給定 UTC 起點過濾 trades 表
/// </summary>
public class TradingDbStorageTests
{
    [Fact]
    public void GetOpenOrders_ReturnsOnlyNonTerminalStatusesWithExternalId()
    {
        using var t = new TestTradingDb();
        var now = DateTime.UtcNow;
        // 各狀態各一筆 + 一筆缺 external_id（poller 不該抓它）
        t.Db.SaveOrder(MakeOrder("o-pending",   "submitted", "ext-1"));
        t.Db.SaveOrder(MakeOrder("o-submitted", "submitted", "ext-2"));
        t.Db.SaveOrder(MakeOrder("o-partial",   "partial",   "ext-3"));
        t.Db.SaveOrder(MakeOrder("o-filled",    "filled",    "ext-4"));   // terminal
        t.Db.SaveOrder(MakeOrder("o-cancelled", "cancelled", "ext-5"));   // terminal
        t.Db.SaveOrder(MakeOrder("o-rejected",  "rejected",  "ext-6"));   // terminal
        t.Db.SaveOrder(MakeOrder("o-no-ext",    "submitted", null));      // 沒 ext_id 不 poll

        var open = t.Db.GetOpenOrders();

        open.Select(o => o.OrderId).Should().BeEquivalentTo(new[] { "o-pending", "o-submitted", "o-partial" });
    }

    [Fact]
    public void GetDailyTradeCount_FromUtcMidnight_CountsTodaysTradesOnly()
    {
        using var t = new TestTradingDb();
        var todayMidnight = DateTime.UtcNow.Date;
        var yesterday = todayMidnight.AddDays(-1).AddHours(12);
        var morning = todayMidnight.AddHours(2);
        var afternoon = todayMidnight.AddHours(14);

        // trades 表 FK 到 orders，每筆 trade 要先有對應的 order 存在
        InsertParentOrder(t, "o-t1");
        InsertParentOrder(t, "o-t2");
        InsertParentOrder(t, "o-t3");
        t.Db.SaveTrade(MakeTrade("t1", yesterday));
        t.Db.SaveTrade(MakeTrade("t2", morning));
        t.Db.SaveTrade(MakeTrade("t3", afternoon));

        t.Db.GetDailyTradeCount(todayMidnight).Should().Be(2);
    }

    [Fact]
    public void GetDailyTradeCount_FilteredByExchange_CountsOnlyMatching()
    {
        using var t = new TestTradingDb();
        var todayMidnight = DateTime.UtcNow.Date;
        var morning = todayMidnight.AddHours(2);

        InsertParentOrder(t, "o-t1", "alpaca");
        InsertParentOrder(t, "o-t2", "binance");
        InsertParentOrder(t, "o-t3", "alpaca");
        t.Db.SaveTrade(MakeTrade("t1", morning, exchange: "alpaca"));
        t.Db.SaveTrade(MakeTrade("t2", morning, exchange: "binance"));
        t.Db.SaveTrade(MakeTrade("t3", morning, exchange: "alpaca"));

        t.Db.GetDailyTradeCount(todayMidnight, exchange: "alpaca").Should().Be(2);
        t.Db.GetDailyTradeCount(todayMidnight, exchange: "binance").Should().Be(1);
        t.Db.GetDailyTradeCount(todayMidnight).Should().Be(3);   // 不過濾就全算
    }

    private static void InsertParentOrder(TestTradingDb t, string orderId, string exchange = "alpaca")
        => t.Db.SaveOrder(MakeOrder(orderId, "filled", $"ext-{orderId}"));

    [Fact]
    public void GetDailyTradeCount_EmptyTable_ReturnsZero()
    {
        using var t = new TestTradingDb();
        t.Db.GetDailyTradeCount(DateTime.UtcNow.Date).Should().Be(0);
    }

    private static TradingOrder MakeOrder(string orderId, string status, string? externalId)
        => new()
        {
            OrderId = orderId, Symbol = "AAPL", Exchange = "alpaca",
            Side = "buy", OrderType = "market", Quantity = 1m, TimeInForce = "gtc",
            Status = status, ExternalId = externalId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

    private static TradeRecord MakeTrade(string id, DateTime executedAt, string exchange = "alpaca")
        => new()
        {
            TradeId = id, OrderId = "o-" + id, Symbol = "AAPL", Exchange = exchange,
            Side = "buy", Quantity = 1m, Price = 100m, ExecutedAt = executedAt,
        };
}
