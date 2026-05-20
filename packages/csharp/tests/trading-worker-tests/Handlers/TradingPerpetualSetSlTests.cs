using System.Text.Json;
using TradingWorker.Exchange;
using TradingWorker.Handlers;
using TradingWorker.Models;

namespace TradingWorker.Tests.Handlers;

/// <summary>
/// #1 — set_position_sl 編排合約（broker BE/trailing 同步用）。
///
/// 這條 route 在真錢路徑上、且 BingX 的 stop 取代行為沒法 unit-test，所以這裡鎖死「我這端的編排」：
///   1. 先下新 stop、再 cancel 舊 stop（避免 cancel 後 place 失敗留裸位）
///   2. 只動 STOP 類單、不碰 TAKE_PROFIT
///   3. 只動同 positionSide 的單、不誤砍另一側
///   4. 新 stop 是 reduce-only STOP_MARKET、價/方向/數量正確
/// 編排對了、剩下就是 demo 帳號驗 BingX 真實行為（那步只有人能做）。
/// </summary>
public class TradingPerpetualSetSlTests
{
    private static IPerpetualClient FakeBingx(List<PerpetualOrder> openOrders, string newStopId = "new-stop-1")
    {
        var c = Substitute.For<IPerpetualClient>();
        c.ExchangeName.Returns("bingx");
        c.GetOpenOrdersAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(openOrders);
        c.PlaceOrderAsync(Arg.Any<PerpetualOrder>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var o = ci.ArgAt<PerpetualOrder>(0); o.ExternalId = newStopId; o.Status = "submitted"; return o; });
        c.CancelOrderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new PerpetualOrder { ExternalId = ci.ArgAt<string>(1), Status = "cancelled" });
        return c;
    }

    private static PerpetualOrder Stop(string id, string posSide) => new()
    {
        ExternalId = id, Symbol = "BTC-USDT", Exchange = "bingx",
        OrderType = "stop_market", PositionSide = posSide, ReduceOnly = true,
    };

    private static PerpetualOrder TakeProfit(string id, string posSide) => new()
    {
        ExternalId = id, Symbol = "BTC-USDT", Exchange = "bingx",
        OrderType = "take_profit_market", PositionSide = posSide, ReduceOnly = true,
    };

    private const string LongPayload =
        """{"exchange":"bingx","symbol":"BTC-USDT","position_side":"long","close_side":"sell","quantity":1,"stop_loss_price":95}""";

    [Fact]
    public async Task ReplacesStop_PlacesNewBeforeCancellingOld()
    {
        var orders = new List<PerpetualOrder> { Stop("old-stop", "long"), TakeProfit("tp-1", "long") };
        var c = FakeBingx(orders);
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        var (success, payload, err) = await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        success.Should().BeTrue(err);
        // 先 place 新 stop、再 cancel 舊 stop —— 順序錯就可能留裸位
        Received.InOrder(() =>
        {
            c.PlaceOrderAsync(Arg.Any<PerpetualOrder>(), Arg.Any<CancellationToken>());
            c.CancelOrderAsync("BTC-USDT", "old-stop", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task PlacesReduceOnlyStopMarket_AtNewPrice_CorrectDirection()
    {
        var c = FakeBingx(new());
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        await c.Received(1).PlaceOrderAsync(Arg.Is<PerpetualOrder>(o =>
            o.OrderType == "stop_market"
            && o.ReduceOnly == true
            && o.StopPrice == 95m
            && o.Side == "sell"
            && o.PositionSide == "long"
            && o.Quantity == 1m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCancelTakeProfit()
    {
        var orders = new List<PerpetualOrder> { Stop("old-stop", "long"), TakeProfit("tp-1", "long") };
        var c = FakeBingx(orders);
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        await c.DidNotReceive().CancelOrderAsync(Arg.Any<string>(), "tp-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCancelOtherSideStop()
    {
        var orders = new List<PerpetualOrder> { Stop("long-stop", "long"), Stop("short-stop", "short") };
        var c = FakeBingx(orders);
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        await c.Received(1).CancelOrderAsync("BTC-USDT", "long-stop", Arg.Any<CancellationToken>());
        await c.DidNotReceive().CancelOrderAsync(Arg.Any<string>(), "short-stop", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeverCancelsTheJustPlacedStop()
    {
        // 防呆：萬一 snapshot 撈到一張 id 跟剛下的新單一樣，不能把新單砍掉
        var orders = new List<PerpetualOrder> { Stop("collision-id", "long") };
        var c = FakeBingx(orders, newStopId: "collision-id");
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        await c.DidNotReceive().CancelOrderAsync(Arg.Any<string>(), "collision-id", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoExistingStops_JustPlacesNew_NoCancel()
    {
        var c = FakeBingx(new());
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        var (success, _, _) = await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        success.Should().BeTrue();
        await c.Received(1).PlaceOrderAsync(Arg.Any<PerpetualOrder>(), Arg.Any<CancellationToken>());
        await c.DidNotReceive().CancelOrderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResponseReportsNewStopAndCancelledOld()
    {
        var orders = new List<PerpetualOrder> { Stop("old-stop", "long") };
        var c = FakeBingx(orders, newStopId: "fresh-stop");
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        var (_, payload, _) = await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        var doc = JsonDocument.Parse(payload!).RootElement;
        doc.GetProperty("new_stop_id").GetString().Should().Be("fresh-stop");
        doc.GetProperty("cancelled_old").EnumerateArray().Select(e => e.GetString()).Should().Contain("old-stop");
    }

    [Fact]
    public async Task CancelFailureDoesNotFailWholeCall_NewStopAlreadyPlaced()
    {
        // cancel 舊單失敗（網路/已成交）不該讓整筆 fail —— 新 stop 已就位才是重點
        var orders = new List<PerpetualOrder> { Stop("stubborn", "long") };
        var c = FakeBingx(orders);
        c.CancelOrderAsync("BTC-USDT", "stubborn", Arg.Any<CancellationToken>())
            .Returns<PerpetualOrder>(_ => throw new Exception("cancel boom"));
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        var (success, payload, _) = await handler.ExecuteAsync("r1", "set_position_sl", LongPayload, "{}", default);

        success.Should().BeTrue("new stop placed; stale cancel error must not naked the position");
        var doc = JsonDocument.Parse(payload!).RootElement;
        doc.GetProperty("cancel_errors").EnumerateArray().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("""{"exchange":"bingx","symbol":"BTC-USDT","position_side":"long","close_side":"sell","quantity":1}""")]      // no sl price
    [InlineData("""{"exchange":"bingx","symbol":"BTC-USDT","position_side":"long","close_side":"sell","stop_loss_price":95}""")] // no qty
    [InlineData("""{"exchange":"bingx","close_side":"sell","quantity":1,"stop_loss_price":95}""")]                              // no symbol/side
    public async Task MissingParams_ReturnsError_NoExchangeCall(string payload)
    {
        var c = FakeBingx(new());
        var handler = new TradingPerpetualHandler(new() { ["bingx"] = c });

        var (success, _, err) = await handler.ExecuteAsync("r1", "set_position_sl", payload, "{}", default);

        success.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
        await c.DidNotReceive().PlaceOrderAsync(Arg.Any<PerpetualOrder>(), Arg.Any<CancellationToken>());
    }
}
