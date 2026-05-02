using Microsoft.Extensions.Logging.Abstractions;
using TradingWorker.Exchange;
using TradingWorker.Models;
using TradingWorker.Services;
using TradingWorker.Tests.Helpers;

namespace TradingWorker.Tests.Services;

/// <summary>
/// FillPoller 的核心契約：
///   - 只 poll 非 terminal 訂單
///   - 把交易所狀態同步回本地 DB
///   - status 變 filled 時寫一筆 trade（trade_id="fill-{ext}" 保證 idempotent）
///   - 重 poll 不會重複寫
///   - 單筆失敗不會中斷整輪
/// </summary>
public class FillPollerServiceTests
{
    [Fact]
    public async Task PollOnce_NoOpenOrders_DoesNothing()
    {
        using var t = new TestTradingDb();
        var alpaca = Substitute.For<IExchangeClient>();
        var poller = MakePoller(t, ("alpaca", alpaca));

        await poller.PollOnceAsync(CancellationToken.None);

        await alpaca.DidNotReceiveWithAnyArgs().GetOrderStatusAsync(default!);
    }

    [Fact]
    public async Task PollOnce_StatusBecomesFilled_UpdatesOrderAndRecordsTrade()
    {
        using var t = new TestTradingDb();
        var executedAt = DateTime.UtcNow;
        t.Db.SaveOrder(MakeOpenOrder("o-1", "ext-1"));

        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.ExchangeName.Returns("alpaca");
        alpaca.GetOrderStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns(MakeFilledOrder("ext-1", filledQty: 1m, filledPrice: 175m, filledAt: executedAt));

        var poller = MakePoller(t, ("alpaca", alpaca));
        await poller.PollOnceAsync(CancellationToken.None);

        var updated = t.Db.GetOrder("o-1")!;
        updated.Status.Should().Be("filled");
        updated.FilledQty.Should().Be(1m);
        updated.FilledPrice.Should().Be(175m);

        var trades = t.Db.GetTrades(symbol: "AAPL");
        trades.Should().ContainSingle();
        trades[0].TradeId.Should().Be("fill-ext-1");
        trades[0].OrderId.Should().Be("o-1");
        trades[0].Quantity.Should().Be(1m);
        trades[0].Price.Should().Be(175m);
    }

    [Fact]
    public async Task PollOnce_RepeatedPoll_DoesNotDuplicateTrade()
    {
        using var t = new TestTradingDb();
        t.Db.SaveOrder(MakeOpenOrder("o-1", "ext-1"));

        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.GetOrderStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns(MakeFilledOrder("ext-1", filledQty: 1m, filledPrice: 175m, filledAt: DateTime.UtcNow));

        var poller = MakePoller(t, ("alpaca", alpaca));

        // 第二次 poll 之前 GetOpenOrders 已經把 o-1 排除（已 filled），所以不會再呼叫交易所——
        // 這個測試其實鎖的是「fill-{ext} idempotent key 設計」+「terminal 排除」共同保證的不重寫
        await poller.PollOnceAsync(CancellationToken.None);
        await poller.PollOnceAsync(CancellationToken.None);

        t.Db.GetTrades(symbol: "AAPL").Should().ContainSingle();
    }

    [Fact]
    public async Task PollOnce_StatusUnchanged_DoesNotInsertTrade()
    {
        using var t = new TestTradingDb();
        t.Db.SaveOrder(MakeOpenOrder("o-1", "ext-1"));

        var alpaca = Substitute.For<IExchangeClient>();
        // 交易所回「還在 submitted」—— 沒進展
        alpaca.GetOrderStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns(new TradingOrder
            {
                OrderId = "ext-1", ExternalId = "ext-1", Symbol = "AAPL", Exchange = "alpaca",
                Side = "buy", OrderType = "market", Quantity = 1m, Status = "submitted",
                FilledQty = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });

        var poller = MakePoller(t, ("alpaca", alpaca));
        await poller.PollOnceAsync(CancellationToken.None);

        t.Db.GetTrades(symbol: "AAPL").Should().BeEmpty();
        t.Db.GetOrder("o-1")!.Status.Should().Be("submitted");
    }

    [Fact]
    public async Task PollOnce_UnknownExchange_SkipsOrderInsteadOfCrashing()
    {
        using var t = new TestTradingDb();
        var order = MakeOpenOrder("o-1", "ext-1");
        order.Exchange = "ftx";   // 假 exchange，clients dict 沒有
        t.Db.SaveOrder(order);

        var poller = MakePoller(t);   // 沒 register 任何 client
        await poller.PollOnceAsync(CancellationToken.None);

        t.Db.GetOrder("o-1")!.Status.Should().Be("submitted");   // 沒被誤改
    }

    [Fact]
    public async Task PollOnce_OneOrderThrows_OtherOrdersStillProcessed()
    {
        using var t = new TestTradingDb();
        t.Db.SaveOrder(MakeOpenOrder("o-broken", "ext-broken"));
        t.Db.SaveOrder(MakeOpenOrder("o-good", "ext-good"));

        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.GetOrderStatusAsync("ext-broken", Arg.Any<CancellationToken>())
            .Returns<Task<TradingOrder?>>(_ => throw new HttpRequestException("transient 503"));
        alpaca.GetOrderStatusAsync("ext-good", Arg.Any<CancellationToken>())
            .Returns(MakeFilledOrder("ext-good", filledQty: 1m, filledPrice: 200m, filledAt: DateTime.UtcNow));

        var poller = MakePoller(t, ("alpaca", alpaca));
        await poller.PollOnceAsync(CancellationToken.None);

        t.Db.GetOrder("o-broken")!.Status.Should().Be("submitted");   // 失敗不動
        t.Db.GetOrder("o-good")!.Status.Should().Be("filled");        // 成功照處理
    }

    [Fact]
    public async Task PollOnce_GetOrderStatusReturnsNull_LeavesOrderAlone()
    {
        using var t = new TestTradingDb();
        t.Db.SaveOrder(MakeOpenOrder("o-1", "ext-1"));

        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.GetOrderStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns((TradingOrder?)null);

        var poller = MakePoller(t, ("alpaca", alpaca));
        await poller.PollOnceAsync(CancellationToken.None);

        t.Db.GetOrder("o-1")!.Status.Should().Be("submitted");
        t.Db.GetTrades().Should().BeEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static FillPollerService MakePoller(TestTradingDb t, params (string Name, IExchangeClient Client)[] clients)
    {
        var dict = clients.ToDictionary(c => c.Name, c => c.Client);
        return new FillPollerService(dict, t.Db, NullLogger<FillPollerService>.Instance);
    }

    private static TradingOrder MakeOpenOrder(string orderId, string externalId)
        => new()
        {
            OrderId = orderId, Symbol = "AAPL", Exchange = "alpaca",
            Side = "buy", OrderType = "market", Quantity = 1m, TimeInForce = "gtc",
            Status = "submitted", ExternalId = externalId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

    private static TradingOrder MakeFilledOrder(string externalId, decimal filledQty, decimal filledPrice, DateTime filledAt)
        => new()
        {
            OrderId = "",   // poller 會用 DB 裡原本的 OrderId 蓋掉
            Symbol = "AAPL", Exchange = "alpaca", Side = "buy", OrderType = "market",
            Quantity = filledQty, Status = "filled",
            FilledQty = filledQty, FilledPrice = filledPrice, ExternalId = externalId,
            CreatedAt = DateTime.UtcNow, FilledAt = filledAt, UpdatedAt = DateTime.UtcNow,
        };
}
