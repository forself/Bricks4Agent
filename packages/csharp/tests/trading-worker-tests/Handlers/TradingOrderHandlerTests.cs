using System.Text.Json;
using TradingWorker.Exchange;
using TradingWorker.Handlers;
using TradingWorker.Models;
using TradingWorker.Tests.Helpers;

namespace TradingWorker.Tests.Handlers;

/// <summary>
/// 驗 idempotency hit 時 payload 帶 `idempotent: true`、新單帶 `false`，
/// 上層 (AutoTrader / DiscordNotificationService) 才能正確區分
/// 「真的下單到交易所」vs「DB dedup 命中、沒打交易所」。
/// </summary>
public class TradingOrderHandlerTests
{
    [Fact]
    public async Task PlaceOrder_FirstCallWithClientOrderId_ReturnsIdempotentFalse()
    {
        using var t = new TestTradingDb();
        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.ExchangeName.Returns("alpaca");
        alpaca.PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var o = ci.ArgAt<TradingOrder>(0);
                o.ExternalId = "ext-1";
                o.Status = "submitted";
                return o;
            });
        var handler = new TradingOrderHandler(new() { ["alpaca"] = alpaca }, t.Db);

        var (success, payload, _) = await handler.ExecuteAsync("req1", "place_order",
            """{"exchange":"alpaca","symbol":"AAPL","side":"buy","quantity":1,"order_type":"market","client_order_id":"test-key-1"}""",
            "{}", default);

        success.Should().BeTrue();
        var doc = JsonDocument.Parse(payload!).RootElement;
        doc.GetProperty("idempotent").GetBoolean().Should().BeFalse("first call should hit the exchange");
        doc.GetProperty("external_id").GetString().Should().Be("ext-1");
        await alpaca.Received(1).PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_SecondCallSameClientOrderId_ReturnsIdempotentTrue_NoExchangeCall()
    {
        using var t = new TestTradingDb();
        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.ExchangeName.Returns("alpaca");
        alpaca.PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var o = ci.ArgAt<TradingOrder>(0);
                o.ExternalId = "ext-1";
                o.Status = "submitted";
                return o;
            });
        var handler = new TradingOrderHandler(new() { ["alpaca"] = alpaca }, t.Db);
        var body = """{"exchange":"alpaca","symbol":"AAPL","side":"buy","quantity":1,"order_type":"market","client_order_id":"test-key-1"}""";
        await handler.ExecuteAsync("req1", "place_order", body, "{}", default);

        alpaca.ClearReceivedCalls();
        var (success, payload, _) = await handler.ExecuteAsync("req2", "place_order", body, "{}", default);

        success.Should().BeTrue();
        var doc = JsonDocument.Parse(payload!).RootElement;
        doc.GetProperty("idempotent").GetBoolean().Should().BeTrue("DB hit should bypass exchange");
        doc.GetProperty("external_id").GetString().Should().Be("ext-1", "should return the original order's external id");
        await alpaca.DidNotReceive().PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_NoClientOrderId_AlwaysReturnsIdempotentFalse()
    {
        // 沒帶 client_order_id 就生新的 ord-{guid}、永遠走交易所
        using var t = new TestTradingDb();
        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.ExchangeName.Returns("alpaca");
        alpaca.PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var o = ci.ArgAt<TradingOrder>(0);
                o.ExternalId = "ext-fresh";
                o.Status = "submitted";
                return o;
            });
        var handler = new TradingOrderHandler(new() { ["alpaca"] = alpaca }, t.Db);

        var (_, payload, _) = await handler.ExecuteAsync("req1", "place_order",
            """{"exchange":"alpaca","symbol":"AAPL","side":"buy","quantity":1,"order_type":"market"}""",
            "{}", default);

        var doc = JsonDocument.Parse(payload!).RootElement;
        doc.GetProperty("idempotent").GetBoolean().Should().BeFalse();
        await alpaca.Received(1).PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_PreviousOrderRejected_RetriesAndDoesNotReturnIdempotent()
    {
        // rejected 是「之前下單失敗」、idempotency 不該擋——讓它重試
        using var t = new TestTradingDb();
        t.Db.SaveOrder(new TradingOrder
        {
            OrderId = "test-rejected", Symbol = "AAPL", Exchange = "alpaca",
            Side = "buy", OrderType = "market", Quantity = 1m, TimeInForce = "gtc",
            Status = "rejected", Error = "previous attempt timed out",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        var alpaca = Substitute.For<IExchangeClient>();
        alpaca.ExchangeName.Returns("alpaca");
        alpaca.PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var o = ci.ArgAt<TradingOrder>(0);
                o.ExternalId = "ext-retry-success";
                o.Status = "submitted";
                return o;
            });
        var handler = new TradingOrderHandler(new() { ["alpaca"] = alpaca }, t.Db);

        var (_, payload, _) = await handler.ExecuteAsync("req1", "place_order",
            """{"exchange":"alpaca","symbol":"AAPL","side":"buy","quantity":1,"order_type":"market","client_order_id":"test-rejected"}""",
            "{}", default);

        var doc = JsonDocument.Parse(payload!).RootElement;
        doc.GetProperty("idempotent").GetBoolean().Should().BeFalse("rejected should allow retry");
        doc.GetProperty("external_id").GetString().Should().Be("ext-retry-success");
        await alpaca.Received(1).PlaceOrderAsync(Arg.Any<TradingOrder>(), Arg.Any<CancellationToken>());
    }
}
