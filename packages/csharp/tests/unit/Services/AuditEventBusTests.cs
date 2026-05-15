using Broker.Services;
using BrokerCore.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Services;

/// <summary>
/// G2 — AuditEventBus pub-sub 行為測試。
///
/// 重點不變式：
/// - 多 subscriber 各自收到完整事件流（fan-out 不分流）
/// - subscriber 用完 unsubscribe 後不再收
/// - subscriber channel 滿（256）→ DropOldest、不阻塞 publisher
/// - publisher 不丟例外（即使 subscriber channel 已 complete）
/// - 0 subscriber 時 publish 也安全
/// </summary>
public class AuditEventBusTests
{
    private static AuditEventBus NewBus() => new(new NullLogger<AuditEventBus>());
    private static AuditEvent E(string type) => new() { EventType = type, EventId = DateTime.UtcNow.Ticks };

    [Fact]
    public void NoSubscribers_PublishIsNoOp()
    {
        var bus = NewBus();
        var act = () => bus.Publish(E("X"));
        act.Should().NotThrow();
        bus.SubscriberCount.Should().Be(0);
    }

    [Fact]
    public async Task SingleSubscriber_ReceivesPublishedEvent()
    {
        var bus = NewBus();
        var (reader, unsub) = bus.Subscribe();
        bus.SubscriberCount.Should().Be(1);

        bus.Publish(E("KILL_SWITCH"));
        var got = await reader.ReadAsync();
        got.EventType.Should().Be("KILL_SWITCH");

        unsub();
        bus.SubscriberCount.Should().Be(0);
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveSameEvents_FanOut()
    {
        var bus = NewBus();
        var (r1, u1) = bus.Subscribe();
        var (r2, u2) = bus.Subscribe();
        var (r3, u3) = bus.Subscribe();
        bus.SubscriberCount.Should().Be(3);

        bus.Publish(E("A"));
        bus.Publish(E("B"));

        (await r1.ReadAsync()).EventType.Should().Be("A");
        (await r2.ReadAsync()).EventType.Should().Be("A");
        (await r3.ReadAsync()).EventType.Should().Be("A");
        (await r1.ReadAsync()).EventType.Should().Be("B");
        (await r2.ReadAsync()).EventType.Should().Be("B");
        (await r3.ReadAsync()).EventType.Should().Be("B");

        u1(); u2(); u3();
    }

    [Fact]
    public async Task UnsubscribedReader_StopsReceiving()
    {
        var bus = NewBus();
        var (reader, unsub) = bus.Subscribe();
        bus.Publish(E("first"));
        (await reader.ReadAsync()).EventType.Should().Be("first");

        unsub();
        bus.Publish(E("second"));   // 沒人收
        bus.SubscriberCount.Should().Be(0);
        // reader 應該已 complete、ReadAllAsync 結束
        var remaining = new List<AuditEvent>();
        await foreach (var ev in reader.ReadAllAsync()) remaining.Add(ev);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ChannelOverflow_DropsOldest_PublisherNotBlocked()
    {
        var bus = NewBus();
        var (reader, unsub) = bus.Subscribe();

        // 灌爆 channel（capacity=256）+ 多塞一些
        const int total = 300;
        for (int i = 0; i < total; i++) bus.Publish(E($"E{i}"));

        // publisher 應該瞬間完成、不阻塞（用 Task.Yield 確保不卡 UI）
        var collected = new List<string>();
        for (int i = 0; i < 256 && reader.TryRead(out var item); i++) collected.Add(item.EventType);

        collected.Should().HaveCountLessThanOrEqualTo(256, "channel cap = 256");
        collected.Last().Should().Be($"E{total - 1}", "DropOldest 模式：最後一筆永遠保留");
        // 第一筆早就被 drop 掉、不該是 E0
        collected.First().Should().NotBe("E0");

        unsub();
        await Task.CompletedTask;
    }

    [Fact]
    public void Unsubscribe_Twice_NoThrow()
    {
        var bus = NewBus();
        var (_, unsub) = bus.Subscribe();
        unsub();
        var act = () => unsub();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SlowSubscriber_DoesNotStarvePublisher()
    {
        var bus = NewBus();
        var (slow, unsubSlow) = bus.Subscribe();
        var (fast, unsubFast) = bus.Subscribe();

        // publisher 連發 1000 筆 — slow 完全不讀、fast 即時讀
        // publisher 不應該等任一邊
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) bus.Publish(E($"E{i}"));
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(500, "publisher 不該因 slow subscriber 阻塞");

        // fast 讀一個能拿到（fan-out 確認）
        fast.TryRead(out var got).Should().BeTrue();
        got!.EventType.Should().StartWith("E");

        unsubSlow(); unsubFast();
        await Task.CompletedTask;
    }
}
