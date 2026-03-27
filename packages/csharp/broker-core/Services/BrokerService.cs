using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Broker 門面服務 —— 核心 PEP（Policy Enforcement Point）
///
/// SubmitExecutionRequest 16 步流程：
///  1. 解析 claims（已由 middleware 完成）
///  2. Epoch 閘道
///  3. Session 狀態檢查
///  4. Idempotency 檢查
///  5. 持久化 ExecutionRequest（state = received）
///  6. Schema 驗證 → validated / denied
///  7. 查詢能力定義
///  8. 檢查 CapabilityGrant
///  9. 檢查配額
/// 10. 檢查時效
/// 11. PolicyEngine.Evaluate()
/// 12. 建立 ApprovedRequest DTO
/// 13. IExecutionDispatcher.DispatchAsync()
/// 14. 收集結果
/// 15. AuditService 記錄
/// 16. 回傳結構化結果
/// </summary>
public class BrokerService : IBrokerService
{
    private readonly BrokerDb _db;
    private readonly IPolicyEngine _policyEngine;
    private readonly IAuditService _auditService;
    private readonly ICapabilityCatalog _capabilityCatalog;
    private readonly ISessionService _sessionService;
    private readonly IRevocationService _revocationService;
    private readonly ITaskRouter _taskRouter;
    private readonly IExecutionDispatcher _executionDispatcher;
    private readonly IToolSpecStatusChecker? _toolSpecStatusChecker;

    public BrokerService(
        BrokerDb db,
        IPolicyEngine policyEngine,
        IAuditService auditService,
        ICapabilityCatalog capabilityCatalog,
        ISessionService sessionService,
        IRevocationService revocationService,
        ITaskRouter taskRouter,
        IExecutionDispatcher executionDispatcher,
        IToolSpecStatusChecker? toolSpecStatusChecker = null)
    {
        _db = db;
        _policyEngine = policyEngine;
        _auditService = auditService;
        _capabilityCatalog = capabilityCatalog;
        _sessionService = sessionService;
        _revocationService = revocationService;
        _taskRouter = taskRouter;
        _executionDispatcher = executionDispatcher;
        _toolSpecStatusChecker = toolSpecStatusChecker;
    }

    /// <inheritdoc />
    public BrokerTask CreateTask(
        string submittedBy,
        string taskType,
        string scopeDescriptor,
        string? assignedPrincipalId = null,
        string? assignedRoleId = null,
        string? runtimeDescriptor = null)
    {
        var riskLevel = _taskRouter.AssessRisk(taskType, scopeDescriptor);

        var task = new BrokerTask
        {
            TaskId = IdGen.New("task"),
            TaskType = taskType,
            SubmittedBy = submittedBy,
            RiskLevel = riskLevel,
            State = TaskState.Created,
            ScopeDescriptor = scopeDescriptor,
            RuntimeDescriptor = string.IsNullOrWhiteSpace(runtimeDescriptor) ? "{}" : runtimeDescriptor,
            AssignedPrincipalId = string.IsNullOrWhiteSpace(assignedPrincipalId) ? null : assignedPrincipalId,
            AssignedRoleId = string.IsNullOrWhiteSpace(assignedRoleId) ? null : assignedRoleId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Insert(task);

        _auditService.RecordEvent(
            traceId: task.TaskId,
            eventType: "TASK_CREATED",
            principalId: submittedBy,
            taskId: task.TaskId,
            details: JsonSerializer.Serialize(new
            {
                taskType,
                riskLevel = riskLevel.ToString(),
                assignedPrincipalId,
                assignedRoleId
            }));

        return task;
    }

    /// <inheritdoc />
    public BrokerTask? GetTask(string taskId)
    {
        return _db.Get<BrokerTask>(taskId);
    }

    /// <inheritdoc />
    public bool CancelTask(string taskId, string cancelledBy, string reason)
    {
        var task = _db.Get<BrokerTask>(taskId);
        if (task == null || task.State is TaskState.Completed or TaskState.Cancelled)
            return false;

        // 更新任務狀態
        _db.Execute(
            "UPDATE broker_tasks SET state = @cancelled, completed_at = @now WHERE task_id = @tid",
            new { cancelled = (int)TaskState.Cancelled, now = DateTime.UtcNow, tid = taskId });

        // 級聯撤銷所有 session
        _sessionService.RevokeSessionsByTask(taskId, reason, cancelledBy);

        _auditService.RecordEvent(
            traceId: taskId,
            eventType: "TASK_CANCELLED",
            principalId: cancelledBy,
            taskId: taskId,
            details: JsonSerializer.Serialize(new { reason }));

        return true;
    }

    /// <inheritdoc />
    public async Task<ExecutionRequest> SubmitExecutionRequestAsync(
        string principalId, string taskId, string sessionId,
        string capabilityId, string intent, string requestPayload,
        string idempotencyKey, string traceId)
    {
        // ── Step 2: Epoch 閘道（已在 BrokerAuthMiddleware 處理，此處雙重檢查） ──
        var currentEpoch = _revocationService.GetCurrentEpoch();

        // ── Step 3: Session 狀態檢查 ──
        var session = _sessionService.GetSession(sessionId);
        if (session == null || session.Status != SessionStatus.Active)
        {
            return CreateDeniedRequest(principalId, taskId, sessionId, capabilityId,
                intent, requestPayload, idempotencyKey, traceId,
                "Session not found or inactive.");
        }

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            return CreateDeniedRequest(principalId, taskId, sessionId, capabilityId,
                intent, requestPayload, idempotencyKey, traceId,
                "Session expired.");
        }

        // ── Step 4: Idempotency 檢查 ──
        var existing = _db.QueryFirst<ExecutionRequest>(
            "SELECT * FROM execution_requests WHERE task_id = @taskId AND idempotency_key = @ikey",
            new { taskId, ikey = idempotencyKey });

        if (existing != null)
        {
            // 回傳既有結果
            return existing;
        }

        // ── Step 5: 持久化 ExecutionRequest（state = received） ──
        var request = new ExecutionRequest
        {
            RequestId = IdGen.New("req"),
            TaskId = taskId,
            SessionId = sessionId,
            PrincipalId = principalId,
            CapabilityId = capabilityId,
            Intent = intent,
            RequestPayload = requestPayload,
            ExecutionState = ExecutionState.Received,
            TraceId = traceId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Insert(request);

        _auditService.RecordEvent(traceId, "EXECUTION_RECEIVED",
            principalId, taskId, sessionId, capabilityId,
            JsonSerializer.Serialize(new { intent, idempotencyKey }));

        // ── Step 6: Schema 驗證（在 PolicyEngine 內執行） ──

        // ── Step 7: 查詢能力定義 ──
        var capability = _capabilityCatalog.GetCapability(capabilityId);
        if (capability == null)
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, $"Capability '{capabilityId}' not found in catalog.");
        }

        // ── Step 7b: 檢查 tool spec status ──
        if (_toolSpecStatusChecker != null)
        {
            var (specAllowed, specReason) = _toolSpecStatusChecker.CheckStatus(capabilityId);
            if (!specAllowed)
            {
                return UpdateRequestState(request, ExecutionState.Denied,
                    Models.PolicyDecision.Deny, specReason ?? "Blocked by tool spec status check.");
            }
        }

        request.ExecutionState = ExecutionState.Validated;
        UpdateState(request);

        // ── Step 8: 檢查 CapabilityGrant ──
        var grant = _capabilityCatalog.GetActiveGrant(principalId, taskId, sessionId, capabilityId);
        if (grant == null)
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, $"No active grant for capability '{capabilityId}' in this task/session.");
        }

        // ── Step 9: 檢查配額 ──
        if (grant.RemainingQuota != -1 && grant.RemainingQuota <= 0)
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, "Grant quota exhausted.");
        }

        // ── Step 10: 檢查時效 ──
        if (grant.ExpiresAt < DateTime.UtcNow)
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, "Grant expired.");
        }

        // ── Step 11: PolicyEngine.Evaluate() ──
        var task = _db.Get<BrokerTask>(taskId);
        if (task == null)
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, $"Task '{taskId}' not found.");
        }

        var policyResult = _policyEngine.Evaluate(
            request, capability, grant, task, currentEpoch, session.EpochAtIssue);

        if (policyResult.Decision != Models.PolicyDecision.Allow)
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                policyResult.Decision, policyResult.Reason);
        }

        request.ExecutionState = ExecutionState.Allowed;
        request.PolicyDecision = Models.PolicyDecision.Allow;
        request.PolicyReason = policyResult.Reason;
        UpdateState(request);

        _auditService.RecordEvent(traceId, "EXECUTION_ALLOWED",
            principalId, taskId, sessionId, capabilityId);

        // ── Step 9 (cont.): 消耗配額 ──
        if (!_capabilityCatalog.ConsumeQuota(grant.GrantId))
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, "Failed to consume grant quota.");
        }

        // ── Step 12: 建立 ApprovedRequest DTO ──
        var approvedRequest = new ApprovedRequest
        {
            RequestId = request.RequestId,
            CapabilityId = capabilityId,
            Route = capability.Route,
            Payload = requestPayload,
            Scope = grant.ScopeOverride,
            TraceId = traceId,
            PrincipalId = principalId,
            TaskId = taskId,
            SessionId = sessionId
        };

        // ── Step 13: Dispatch ──
        request.ExecutionState = ExecutionState.Dispatched;
        UpdateState(request);

        _auditService.RecordEvent(traceId, "EXECUTION_DISPATCHED",
            principalId, taskId, sessionId, capabilityId);

        // ── Step 14: 收集結果（H-3 修復：proper async，消除 sync-over-async） ──
        ExecutionResult executionResult;
        try
        {
            executionResult = await _executionDispatcher.DispatchAsync(approvedRequest);
        }
        catch (Exception ex)
        {
            executionResult = ExecutionResult.Fail(request.RequestId, ex.Message);
        }

        // ── Step 15: 更新狀態 + 稽核 ──
        if (executionResult.Success)
        {
            request.ExecutionState = ExecutionState.Succeeded;
            request.ResultPayload = executionResult.ResultPayload;
            request.EvidenceRef = executionResult.EvidenceRef;

            _auditService.RecordEvent(traceId, "EXECUTION_SUCCEEDED",
                principalId, taskId, sessionId, capabilityId);
        }
        else
        {
            request.ExecutionState = ExecutionState.Failed;
            request.ResultPayload = JsonSerializer.Serialize(new { error = executionResult.ErrorMessage });

            _auditService.RecordEvent(traceId, "EXECUTION_FAILED",
                principalId, taskId, sessionId, capabilityId,
                JsonSerializer.Serialize(new { error = executionResult.ErrorMessage }));
        }

        request.UpdatedAt = DateTime.UtcNow;
        UpdateState(request);

        // ── Step 16: 回傳 ──
        return request;
    }

    /// <inheritdoc />
    public ExecutionRequest? GetExecutionRequest(string requestId)
    {
        return _db.Get<ExecutionRequest>(requestId);
    }

    // ── 內部方法 ──

    private ExecutionRequest CreateDeniedRequest(
        string principalId, string taskId, string sessionId, string capabilityId,
        string intent, string requestPayload, string idempotencyKey, string traceId,
        string reason)
    {
        var request = new ExecutionRequest
        {
            RequestId = IdGen.New("req"),
            TaskId = taskId,
            SessionId = sessionId,
            PrincipalId = principalId,
            CapabilityId = capabilityId,
            Intent = intent,
            RequestPayload = requestPayload,
            ExecutionState = ExecutionState.Denied,
            PolicyDecision = Models.PolicyDecision.Deny,
            PolicyReason = reason,
            TraceId = traceId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Insert(request);

        _auditService.RecordEvent(traceId, "EXECUTION_DENIED",
            principalId, taskId, sessionId, capabilityId,
            JsonSerializer.Serialize(new { reason }));

        return request;
    }

    private ExecutionRequest UpdateRequestState(
        ExecutionRequest request, ExecutionState state,
        Models.PolicyDecision decision, string reason)
    {
        request.ExecutionState = state;
        request.PolicyDecision = decision;
        request.PolicyReason = reason;
        request.UpdatedAt = DateTime.UtcNow;
        UpdateState(request);

        _auditService.RecordEvent(request.TraceId,
            $"EXECUTION_{state.ToString().ToUpperInvariant()}",
            request.PrincipalId, request.TaskId, request.SessionId, request.CapabilityId,
            JsonSerializer.Serialize(new { reason }));

        return request;
    }

    private void UpdateState(ExecutionRequest request)
    {
        _db.Execute(
            @"UPDATE execution_requests
              SET execution_state = @state, policy_decision = @decision, policy_reason = @reason,
                  result_payload = @result, evidence_ref = @evidence, updated_at = @now
              WHERE request_id = @rid",
            new
            {
                state = request.ExecutionStateValue,
                decision = request.PolicyDecisionValue,
                reason = request.PolicyReason,
                result = request.ResultPayload,
                evidence = request.EvidenceRef,
                now = request.UpdatedAt,
                rid = request.RequestId
            });
    }
}
