using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 核准服務——管理 approval_requests 表 + 提供「這個 capability 需要 approve 嗎」的 policy。
///
/// PoolDispatcher 派發前呼叫 GetOrCreatePending() 看狀態：
///   - null → 不需要 approve、放行
///   - status='pending' → 寫了一筆等審、Fail caller
///   - status='approved' → 放行
///   - status='rejected' → Fail caller
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// 查 capability 是否需要 approve（policy）。null = 不需要；non-null = 走 approve 流程。
    /// </summary>
    bool RequiresApproval(string capabilityId);

    /// <summary>
    /// 對給定 trace_id 取得 / 建立 approval。
    /// 第一次呼叫產 'pending'；之後同 trace_id 直接回上次的 record。
    /// </summary>
    ApprovalRequest GetOrCreatePending(
        string traceId, string capabilityId, string route, string payload,
        string principalId, string role);

    /// <summary>列 approval（依 status 過濾、新到舊）</summary>
    List<ApprovalRequest> List(string? status = null, int limit = 50);

    /// <summary>查單一 approval</summary>
    ApprovalRequest? Get(string approvalId);

    /// <summary>標 approved</summary>
    bool Approve(string approvalId, string decidedBy, string? reason = null);

    /// <summary>標 rejected</summary>
    bool Reject(string approvalId, string decidedBy, string? reason = null);
}

/// <summary>
/// 預設實作：policy 寫死、storage 走 BrokerDb。
///
/// 預設規則（KISS）：
///   trading.order → 需要 approve（實單下單；多用戶階段擋下手動下單 surface area）
///   其餘 → 直接放行
///
/// trading.perpetual / trading.account 不在內、AutoTrader 仍能照常跑。
/// 之後想加更多受控 capability 直接補進這個集合。
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly BrokerDb _db;

    private static readonly HashSet<string> RequiringApproval =
        new(StringComparer.OrdinalIgnoreCase) { "trading.order" };

    public ApprovalService(BrokerDb db) { _db = db; }

    public bool RequiresApproval(string capabilityId)
        => RequiringApproval.Contains(capabilityId);

    public ApprovalRequest GetOrCreatePending(
        string traceId, string capabilityId, string route, string payload,
        string principalId, string role)
    {
        // 同 trace_id 已經有紀錄 → 回原本的（不重複建）
        var existing = _db.QueryFirst<ApprovalRequest>(
            "SELECT * FROM approval_requests WHERE trace_id = @traceId LIMIT 1",
            new { traceId });
        if (existing != null) return existing;

        var req = new ApprovalRequest
        {
            ApprovalId = IdGen.New("apr"),
            TraceId = traceId,
            CapabilityId = capabilityId,
            Route = route,
            Payload = payload,
            PrincipalId = principalId,
            Role = role,
            RequestedAt = DateTime.UtcNow,
            Status = "pending",
        };
        _db.Insert(req);
        return req;
    }

    public List<ApprovalRequest> List(string? status = null, int limit = 50)
    {
        if (string.IsNullOrEmpty(status))
            return _db.Query<ApprovalRequest>(
                "SELECT * FROM approval_requests ORDER BY requested_at DESC LIMIT @limit",
                new { limit });
        return _db.Query<ApprovalRequest>(
            "SELECT * FROM approval_requests WHERE status = @status ORDER BY requested_at DESC LIMIT @limit",
            new { status, limit });
    }

    public ApprovalRequest? Get(string approvalId)
        => _db.QueryFirst<ApprovalRequest>(
            "SELECT * FROM approval_requests WHERE approval_id = @id",
            new { id = approvalId });

    public bool Approve(string approvalId, string decidedBy, string? reason = null)
        => UpdateStatus(approvalId, "approved", decidedBy, reason);

    public bool Reject(string approvalId, string decidedBy, string? reason = null)
        => UpdateStatus(approvalId, "rejected", decidedBy, reason);

    private bool UpdateStatus(string approvalId, string newStatus, string decidedBy, string? reason)
    {
        var existing = Get(approvalId);
        if (existing == null) return false;
        if (existing.Status != "pending") return false;  // 已決定 → 不能重決
        existing.Status = newStatus;
        existing.DecidedBy = decidedBy;
        existing.DecidedAt = DateTime.UtcNow;
        existing.DecisionReason = reason;
        _db.Update(existing);
        return true;
    }
}
