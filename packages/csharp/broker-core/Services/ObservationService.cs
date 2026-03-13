using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 觀測服務實作 —— 持久化 + Correlation 查詢 + 稽核整合
///
/// 每條觀測事件同時產生一條 AuditEvent（OBSERVATION_RECORDED），
/// 確保觀測記錄也納入不可變稽核鏈。
/// </summary>
public class ObservationService : IObservationService
{
    private readonly BrokerDb _db;
    private readonly IAuditService _auditService;

    public ObservationService(BrokerDb db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    public ObservationEvent Record(ObservationEvent observation)
    {
        // 確保 ID
        if (string.IsNullOrEmpty(observation.ObservationId))
            observation.ObservationId = IdGen.New("obs");

        if (observation.ObservedAt == default)
            observation.ObservedAt = DateTime.UtcNow;

        _db.Insert(observation);

        // 整合稽核：每條觀測也產生 AuditEvent
        _auditService.RecordEvent(
            traceId: observation.TraceId,
            eventType: "OBSERVATION_RECORDED",
            principalId: observation.PrincipalId,
            taskId: null,
            details: JsonSerializer.Serialize(new
            {
                observationId = observation.ObservationId,
                observationType = observation.EventType,
                source = observation.Source.ToString(),
                severity = observation.Severity.ToString(),
                planId = observation.PlanId,
                nodeId = observation.NodeId,
                requestId = observation.RequestId,
                workerId = observation.WorkerId
            }));

        return observation;
    }

    // L-4 修復：所有查詢加入 LIMIT/OFFSET 分頁，防止大量資料一次載入導致 OOM

    public List<ObservationEvent> GetByTrace(string traceId, int limit = 200, int offset = 0)
    {
        return _db.Query<ObservationEvent>(
            "SELECT * FROM observation_events WHERE trace_id = @tid ORDER BY observed_at LIMIT @limit OFFSET @offset",
            new { tid = traceId, limit, offset });
    }

    public List<ObservationEvent> GetByPlan(string planId, int limit = 200, int offset = 0)
    {
        return _db.Query<ObservationEvent>(
            "SELECT * FROM observation_events WHERE plan_id = @pid ORDER BY observed_at LIMIT @limit OFFSET @offset",
            new { pid = planId, limit, offset });
    }

    public List<ObservationEvent> GetByNode(string nodeId, int limit = 200, int offset = 0)
    {
        return _db.Query<ObservationEvent>(
            "SELECT * FROM observation_events WHERE node_id = @nid ORDER BY observed_at LIMIT @limit OFFSET @offset",
            new { nid = nodeId, limit, offset });
    }

    public List<ObservationEvent> GetAlerts(DateTime since, ObservationSeverity minSeverity, int limit = 200, int offset = 0)
    {
        return _db.Query<ObservationEvent>(
            "SELECT * FROM observation_events WHERE severity >= @sev AND observed_at >= @since ORDER BY observed_at DESC LIMIT @limit OFFSET @offset",
            new { sev = (int)minSeverity, since, limit, offset });
    }
}
