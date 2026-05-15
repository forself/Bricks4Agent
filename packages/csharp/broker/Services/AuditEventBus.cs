using System.Collections.Concurrent;
using System.Threading.Channels;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// G2 — Audit event in-process pub-sub bus
///
/// 給 dashboard live event stream WebSocket 用。
/// AuditService.RecordEvent 寫進 DB 之後、廣播一份到所有訂閱者的 channel。
///
/// 為什麼不直接 polling DB：每秒查 DB 拿 max(event_id) 做 diff 浪費 SQL +
/// 看不到當下 hash chain 「鏈」起來的視覺感（事件冒出來的瞬間就推）。
///
/// 設計：
/// - 每個 subscriber 一條 unbounded Channel（FullMode = DropOldest 避免無限長）
/// - subscribe 回傳 (channel reader, dispose action)
/// - publisher (BroadcastingAuditService) 不阻塞、subscriber 慢就丟舊事件
///
/// 為什麼選 Channel：BCL 內建、有 backpressure 概念、比手刻 List<Action> 安全。
/// </summary>
public class AuditEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<AuditEvent>> _subscribers = new();
    private readonly ILogger<AuditEventBus> _logger;
    private const int PerSubscriberCapacity = 256;

    public AuditEventBus(ILogger<AuditEventBus> logger) { _logger = logger; }

    public int SubscriberCount => _subscribers.Count;

    /// <summary>訂閱、回 (reader, unsubscribe)。subscriber 用完務必呼叫 unsubscribe。</summary>
    public (ChannelReader<AuditEvent> Reader, Action Unsubscribe) Subscribe()
    {
        var ch = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(PerSubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = ch;
        return (ch.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed))
                removed.Writer.TryComplete();
        });
    }

    /// <summary>廣播；不丟 exception、不阻塞。subscriber channel 滿就 DropOldest。</summary>
    public void Publish(AuditEvent ev)
    {
        if (_subscribers.IsEmpty) return;
        foreach (var (id, ch) in _subscribers)
        {
            try
            {
                if (!ch.Writer.TryWrite(ev))
                    _logger.LogDebug("AuditEventBus: subscriber {Id} channel full, dropped event {Type}", id, ev.EventType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuditEventBus: publish failed for subscriber {Id}", id);
            }
        }
    }
}
