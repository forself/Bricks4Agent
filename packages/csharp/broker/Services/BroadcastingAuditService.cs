using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// G2 — IAuditService decorator：寫進 Benson 的 AuditService 之後、廣播一份到 AuditEventBus。
///
/// 為什麼 decorator 不直接改 BrokerCore.AuditService：Benson 原作不動原則。
/// 用 DI 重註冊把這個包在外面、所有 caller 用 IAuditService 就會走廣播路徑、
/// AuditService 本身（hash chain 邏輯）一行不改。
///
/// 廣播失敗不影響審計寫入（內層先寫 DB、外層 try-catch 廣播）—— audit 可靠性 > stream 可靠性。
/// </summary>
public class BroadcastingAuditService : IAuditService
{
    private readonly IAuditService _inner;
    private readonly AuditEventBus _bus;
    private readonly ILogger<BroadcastingAuditService> _logger;

    public BroadcastingAuditService(IAuditService inner, AuditEventBus bus,
        ILogger<BroadcastingAuditService> logger)
    {
        _inner = inner;
        _bus = bus;
        _logger = logger;
    }

    public AuditEvent RecordEvent(string traceId, string eventType,
        string? principalId = null, string? taskId = null, string? sessionId = null,
        string? resourceRef = null, string details = "{}")
    {
        var ev = _inner.RecordEvent(traceId, eventType, principalId, taskId, sessionId, resourceRef, details);
        try { _bus.Publish(ev); }
        catch (Exception ex) { _logger.LogWarning(ex, "AuditEventBus publish failed for {Type}", eventType); }
        return ev;
    }

    // 純 read-through，不需廣播
    public List<AuditEvent> GetTraceEvents(string traceId)
        => _inner.GetTraceEvents(traceId);

    public bool VerifyTraceIntegrity(string traceId)
        => _inner.VerifyTraceIntegrity(traceId);

    public List<AuditEvent> QueryEvents(string? eventType = null, string? principalId = null,
        string? taskId = null, int offset = 0, int limit = 50)
        => _inner.QueryEvents(eventType, principalId, taskId, offset, limit);

    public List<TraceSummary> ListRecentTraces(string? principalId = null, string? capabilityId = null,
        int offset = 0, int limit = 50, bool includeHttp = false)
        => _inner.ListRecentTraces(principalId, capabilityId, offset, limit, includeHttp);

    public List<TopologyEdge> GetTopology(int sinceMinutes = 60)
        => _inner.GetTopology(sinceMinutes);

    public List<CapabilityLatencyStats> GetLatencyStats(int sinceMinutes = 60)
        => _inner.GetLatencyStats(sinceMinutes);
}
