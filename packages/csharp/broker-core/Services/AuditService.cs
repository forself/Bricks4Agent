using System.Security.Cryptography;
using System.Text;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 追加式稽核服務 —— per-trace hash chain
///
/// 並發語意：
/// - 同 trace 內靠 exclusive transaction + UNIQUE(trace_id, trace_seq) 序列化
/// - 跨 trace 不阻塞
/// - 只 INSERT 不 UPDATE/DELETE → 防篡改
///
/// Hash Chain：
/// event_hash = SHA256(previous_event_hash + trace_id + trace_seq + event_type + payload_digest + occurred_at)
/// 首筆事件的 previous_event_hash = "GENESIS"
/// </summary>
public class AuditService : IAuditService
{
    private readonly BrokerDb _db;

    public AuditService(BrokerDb db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public AuditEvent RecordEvent(
        string traceId,
        string eventType,
        string? principalId = null,
        string? taskId = null,
        string? sessionId = null,
        string? resourceRef = null,
        string details = "{}")
    {
        var payloadDigest = ComputeSha256(details);
        var occurredAt = DateTime.UtcNow;

        // exclusive transaction 保證同 trace 寫入序列化
        return _db.InTransaction(() =>
        {
            // 取得當前 trace 的最後一筆事件
            var lastEvent = _db.QueryFirst<AuditEvent>(
                "SELECT * FROM audit_events WHERE trace_id = @traceId ORDER BY trace_seq DESC LIMIT 1",
                new { traceId });

            var traceSeq = lastEvent != null ? lastEvent.TraceSeq + 1 : 0;
            var previousHash = lastEvent?.EventHash ?? "GENESIS";

            // 計算 event_hash
            var hashInput = $"{previousHash}|{traceId}|{traceSeq}|{eventType}|{payloadDigest}|{occurredAt:O}";
            var eventHash = ComputeSha256(hashInput);

            var auditEvent = new AuditEvent
            {
                TraceId = traceId,
                TraceSeq = traceSeq,
                EventType = eventType,
                PrincipalId = principalId,
                TaskId = taskId,
                SessionId = sessionId,
                ResourceRef = resourceRef,
                PayloadDigest = payloadDigest,
                PreviousEventHash = previousHash,
                EventHash = eventHash,
                Details = details,
                OccurredAt = occurredAt
            };

            _db.Insert(auditEvent);
            return auditEvent;
        });
    }

    /// <inheritdoc />
    public List<AuditEvent> GetTraceEvents(string traceId)
    {
        return _db.Query<AuditEvent>(
            "SELECT * FROM audit_events WHERE trace_id = @traceId ORDER BY trace_seq ASC",
            new { traceId });
    }

    /// <inheritdoc />
    public bool VerifyTraceIntegrity(string traceId)
    {
        var events = GetTraceEvents(traceId);

        if (events.Count == 0)
            return true; // 空 trace 視為完整

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];

            // 驗證序號連續
            if (ev.TraceSeq != i)
                return false;

            // 驗證 previous_event_hash 鏈結
            var expectedPrevHash = i == 0 ? "GENESIS" : events[i - 1].EventHash;
            if (ev.PreviousEventHash != expectedPrevHash)
                return false;

            // 重新計算 hash 並驗證
            var hashInput = $"{ev.PreviousEventHash}|{ev.TraceId}|{ev.TraceSeq}|{ev.EventType}|{ev.PayloadDigest}|{ev.OccurredAt:O}";
            var expectedHash = ComputeSha256(hashInput);
            if (ev.EventHash != expectedHash)
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public List<AuditEvent> QueryEvents(
        string? eventType = null, string? principalId = null,
        string? taskId = null, int offset = 0, int limit = 50)
    {
        var sql = new StringBuilder("SELECT * FROM audit_events WHERE 1=1");
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(eventType))
        {
            sql.Append(" AND event_type = @eventType");
            parameters["eventType"] = eventType;
        }
        if (!string.IsNullOrEmpty(principalId))
        {
            sql.Append(" AND principal_id = @principalId");
            parameters["principalId"] = principalId;
        }
        if (!string.IsNullOrEmpty(taskId))
        {
            sql.Append(" AND task_id = @taskId");
            parameters["taskId"] = taskId;
        }

        sql.Append(" ORDER BY event_id DESC LIMIT @limit OFFSET @offset");
        parameters["limit"] = limit;
        parameters["offset"] = offset;

        return _db.Query<AuditEvent>(sql.ToString(), parameters);
    }

    /// <inheritdoc />
    public List<TraceSummary> ListRecentTraces(
        string? principalId = null, string? capabilityId = null,
        int offset = 0, int limit = 50, bool includeHttp = false)
    {
        // 子查詢：trace_id + 起訖事件 + 事件數
        // 外查詢：抓那個 trace 的最後一筆完整資訊（用 MAX(event_id)）作代表行
        // SQLite 不支援 first_value、所以用 sub-query join 取最後一筆
        //
        // dispatcher-only filter (預設)：只列「至少有一筆 DISPATCH_* / EXECUTION_*」的 trace、
        // 把 ASP.NET HTTP middleware 寫的 W3C trace（API_REQUEST / API_RESPONSE 之類純 HTTP 事件）
        // 排掉、避免 dashboard 自己 polling /audit/traces 一直把列表灌爆。想看全部 → includeHttp=true。
        var dispatcherFilter = includeHttp ? "" : @"
    AND trace_id IN (
        SELECT DISTINCT trace_id FROM audit_events
        WHERE event_type LIKE 'DISPATCH_%' OR event_type LIKE 'EXECUTION_%'
    )";
        var sql = new StringBuilder(@"
SELECT
    g.trace_id              AS TraceId,
    g.first_at              AS FirstAt,
    g.last_at               AS LastAt,
    g.event_count           AS EventCount,
    last_ev.event_type      AS LastEventType,
    first_ev.event_type     AS FirstEventType,
    last_ev.principal_id    AS PrincipalId,
    last_ev.task_id         AS TaskId,
    last_ev.resource_ref    AS CapabilityId
FROM (
    SELECT trace_id,
           MIN(occurred_at) AS first_at,
           MAX(occurred_at) AS last_at,
           MIN(event_id)    AS first_event_id,
           MAX(event_id)    AS last_event_id,
           COUNT(*)         AS event_count
    FROM audit_events
    WHERE 1=1" + dispatcherFilter + @"
    GROUP BY trace_id
) g
JOIN audit_events last_ev  ON last_ev.event_id  = g.last_event_id
JOIN audit_events first_ev ON first_ev.event_id = g.first_event_id
WHERE 1=1
");
        var parameters = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(principalId))
        {
            sql.Append(" AND last_ev.principal_id = @principalId");
            parameters["principalId"] = principalId;
        }
        if (!string.IsNullOrEmpty(capabilityId))
        {
            sql.Append(" AND last_ev.resource_ref = @capabilityId");
            parameters["capabilityId"] = capabilityId;
        }
        sql.Append(" ORDER BY g.last_event_id DESC LIMIT @limit OFFSET @offset");
        parameters["limit"] = limit;
        parameters["offset"] = offset;

        var rows = _db.Query<TraceSummary>(sql.ToString(), parameters);

        // 計算 duration_ms 跟狀態（last_event_type 後綴判斷）
        foreach (var r in rows)
        {
            r.DurationMs = (long)(r.LastAt - r.FirstAt).TotalMilliseconds;
            r.Status = r.LastEventType switch
            {
                var s when s != null && s.EndsWith("_SUCCEEDED", StringComparison.Ordinal) => "SUCCEEDED",
                var s when s != null && s.EndsWith("_FAILED",    StringComparison.Ordinal) => "FAILED",
                var s when s != null && s.EndsWith("_DENIED",    StringComparison.Ordinal) => "DENIED",
                _ => "IN_PROGRESS",
            };
        }
        return rows;
    }

    /// <inheritdoc />
    public List<TopologyEdge> GetTopology(int sinceMinutes = 60)
    {
        // 只看 dispatch + execution 事件、把噪音（TASK_CREATED 等）排除
        // 一個 (principal, capability) 配對：calls = STARTED 事件數，
        // success = SUCCEEDED 事件數，failed = FAILED + DENIED 事件數
        var since = DateTime.UtcNow.AddMinutes(-sinceMinutes);
        var rows = _db.Query<EdgeRow>(@"
SELECT
    COALESCE(principal_id, '(unknown)') AS Principal,
    COALESCE(resource_ref, '(unknown)') AS Capability,
    SUM(CASE WHEN event_type LIKE '%_STARTED' OR event_type = 'EXECUTION_DISPATCHED' THEN 1 ELSE 0 END) AS Started,
    SUM(CASE WHEN event_type LIKE '%_SUCCEEDED' THEN 1 ELSE 0 END) AS Succeeded,
    SUM(CASE WHEN event_type LIKE '%_FAILED' OR event_type LIKE '%_DENIED' THEN 1 ELSE 0 END) AS Failed
FROM audit_events
WHERE occurred_at > @since
  AND (event_type LIKE 'DISPATCH_%' OR event_type LIKE 'EXECUTION_%')
GROUP BY principal_id, resource_ref
ORDER BY Started DESC, Succeeded DESC
",
            new { since });

        return rows
            .Where(r => r.Started > 0 || r.Succeeded > 0 || r.Failed > 0)
            .Select(r => new TopologyEdge
            {
                Principal      = r.Principal,
                Capability     = r.Capability,
                // 用 STARTED 數估總呼叫；如果只有 SUCCEEDED 沒 STARTED（例如 16 步驟 PEP 沒記 STARTED 事件）
                // 就 fallback 到 Succeeded+Failed
                CallsTotal     = r.Started > 0 ? r.Started : (r.Succeeded + r.Failed),
                CallsSucceeded = r.Succeeded,
                CallsFailed    = r.Failed,
            })
            .ToList();
    }

    private class EdgeRow
    {
        public string Principal { get; set; } = string.Empty;
        public string Capability { get; set; } = string.Empty;
        public int Started { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
    }

    /// <inheritdoc />
    public List<CapabilityLatencyStats> GetLatencyStats(int sinceMinutes = 60)
    {
        // 拉所有 DISPATCH_SUCCEEDED / DISPATCH_FAILED（這兩種 event 的 details JSON 裡才會有 duration_ms）
        // 失敗事件的 duration_ms 也算進去——latency 不是只有「成功才算」
        var since = DateTime.UtcNow.AddMinutes(-sinceMinutes);
        var rows = _db.Query<DurationRow>(@"
SELECT
    COALESCE(resource_ref, '(unknown)')              AS Capability,
    event_type                                       AS EventType,
    json_extract(details, '$.duration_ms')           AS DurationMs
FROM audit_events
WHERE occurred_at > @since
  AND (event_type = 'DISPATCH_SUCCEEDED' OR event_type = 'DISPATCH_FAILED')
  AND json_extract(details, '$.duration_ms') IS NOT NULL
",
            new { since });

        // 分組並計算 percentile + bucket
        return rows
            .GroupBy(r => r.Capability)
            .Select(g =>
            {
                var durations = g.Select(r => r.DurationMs).OrderBy(d => d).ToList();
                var n = durations.Count;
                var succeeded = g.Count(r => r.EventType == "DISPATCH_SUCCEEDED");
                var failed    = g.Count(r => r.EventType == "DISPATCH_FAILED");

                return new CapabilityLatencyStats
                {
                    CapabilityId = g.Key,
                    Calls = n, Succeeded = succeeded, Failed = failed,
                    P50Ms = Percentile(durations, 50),
                    P95Ms = Percentile(durations, 95),
                    P99Ms = Percentile(durations, 99),
                    MaxMs = durations[^1],
                    AvgMs = (long)durations.Average(),
                    Distribution = BucketDistribution(durations),
                };
            })
            .OrderByDescending(s => s.Calls)
            .ToList();
    }

    private static long Percentile(List<long> sortedAsc, int pct)
    {
        if (sortedAsc.Count == 0) return 0;
        var idx = Math.Min(sortedAsc.Count - 1, (int)Math.Ceiling(sortedAsc.Count * pct / 100.0) - 1);
        return sortedAsc[Math.Max(0, idx)];
    }

    /// <summary>固定 7 桶：&lt;10/10-50/50-100/100-500/500-1000/1000-5000/&gt;5000 ms。</summary>
    private static readonly (string Label, long Lower, long Upper)[] BucketDefs = new[]
    {
        ("<10ms",       0L,    10L),
        ("10-50ms",     10L,   50L),
        ("50-100ms",    50L,   100L),
        ("100-500ms",   100L,  500L),
        ("500ms-1s",    500L,  1_000L),
        ("1-5s",        1_000L, 5_000L),
        (">5s",         5_000L, long.MaxValue),
    };

    private static List<LatencyBucket> BucketDistribution(List<long> sortedAsc)
    {
        var result = BucketDefs
            .Select(b => new LatencyBucket { Label = b.Label, LowerMs = b.Lower, UpperMs = b.Upper, Count = 0 })
            .ToList();
        foreach (var d in sortedAsc)
        {
            for (int i = 0; i < BucketDefs.Length; i++)
            {
                var (_, lo, hi) = BucketDefs[i];
                if (d >= lo && d < hi) { result[i].Count++; break; }
                // edge: max bucket includes >= 5000
                if (i == BucketDefs.Length - 1 && d >= lo) { result[i].Count++; break; }
            }
        }
        return result;
    }

    private class DurationRow
    {
        public string Capability { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public long DurationMs { get; set; }
    }

    // ── 內部方法 ──

    private static string ComputeSha256(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
