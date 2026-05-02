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

    [Fact]
    public void GetLastTradeTimePerSymbol_GroupsByExchangeAndSymbol_ReturnsLatestPerKey()
    {
        using var t = new TestTradingDb();
        var earlier = DateTime.UtcNow.AddMinutes(-30);
        var later = DateTime.UtcNow.AddMinutes(-5);

        // 同 (alpaca, AAPL) 兩筆，要回 later 那筆
        InsertParentOrder(t, "o-aapl-1");
        InsertParentOrder(t, "o-aapl-2");
        t.Db.SaveTrade(MakeTrade("aapl-1", earlier, exchange: "alpaca", symbol: "AAPL"));
        t.Db.SaveTrade(MakeTrade("aapl-2", later,   exchange: "alpaca", symbol: "AAPL"));
        // 不同交易所同 symbol 應該分開算
        InsertParentOrder(t, "o-btc", "binance");
        t.Db.SaveTrade(MakeTrade("btc", earlier, exchange: "binance", symbol: "BTC"));

        var dict = t.Db.GetLastTradeTimePerSymbol();

        dict.Should().HaveCount(2);
        dict["alpaca:AAPL"].Should().BeCloseTo(later, TimeSpan.FromSeconds(1));
        dict["binance:BTC"].Should().BeCloseTo(earlier, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetLastTradeTimePerSymbol_EmptyTable_ReturnsEmptyDict()
    {
        using var t = new TestTradingDb();
        t.Db.GetLastTradeTimePerSymbol().Should().BeEmpty();
    }

    private static TradingOrder MakeOrder(string orderId, string status, string? externalId)
        => new()
        {
            OrderId = orderId, Symbol = "AAPL", Exchange = "alpaca",
            Side = "buy", OrderType = "market", Quantity = 1m, TimeInForce = "gtc",
            Status = status, ExternalId = externalId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

    private static TradeRecord MakeTrade(string id, DateTime executedAt, string exchange = "alpaca", string symbol = "AAPL")
        => new()
        {
            TradeId = id, OrderId = "o-" + id, Symbol = symbol, Exchange = exchange,
            Side = "buy", Quantity = 1m, Price = 100m, ExecutedAt = executedAt,
        };
}
