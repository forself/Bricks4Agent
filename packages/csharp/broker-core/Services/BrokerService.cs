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

        // ── Step 11b: RequireApproval → 建審批請求並擱置(§18.2) ──
        if (policyResult.Decision == Models.PolicyDecision.RequireApproval)
        {
            var approval = new ApprovalRequest
            {
                ApprovalId = IdGen.New("apr"),
                RequestId = request.RequestId,
                TaskId = taskId,
                SessionId = sessionId,
                PrincipalId = principalId,
                CapabilityId = capabilityId,
                Reason = policyResult.Reason,
                Status = ApprovalStatus.Pending,
                ApproverTier = policyResult.RequiredApproverTier,
                OwnerPrincipalId = principalId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddSeconds(capability.TtlSeconds > 0 ? capability.TtlSeconds : 900),
                TraceId = traceId
            };
            _db.Insert(approval);

            request.ExecutionState = ExecutionState.PendingApproval;
            request.PolicyDecision = Models.PolicyDecision.RequireApproval;
            request.PolicyReason = policyResult.Reason;
            request.UpdatedAt = DateTime.UtcNow;
            UpdateState(request);

            _auditService.RecordEvent(traceId, "APPROVAL_REQUESTED",
                principalId, taskId, sessionId, capabilityId,
                JsonSerializer.Serialize(new { approval_id = approval.ApprovalId, reason = policyResult.Reason }));

            return request;
        }

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

        // ── Steps 12-16:消耗配額 + dispatch + 記錄結果(與審批通過後共用) ──
        return await DispatchAndRecordAsync(request, capability, grant);
    }

    /// <summary>
    /// Steps 12-16:消耗配額 + 建 ApprovedRequest + dispatch + 記錄結果。
    /// SubmitExecutionRequestAsync 的 Allow 路徑與 ApproveExecutionAsync 審批通過後共用。
    /// </summary>
    private async Task<ExecutionRequest> DispatchAndRecordAsync(
        ExecutionRequest request, Capability capability, CapabilityGrant grant)
    {
        if (!_capabilityCatalog.ConsumeQuota(grant.GrantId))
        {
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, "Failed to consume grant quota.");
        }

        var approvedRequest = new ApprovedRequest
        {
            RequestId = request.RequestId,
            CapabilityId = request.CapabilityId,
            Route = capability.Route,
            Payload = request.RequestPayload,
            Scope = grant.ScopeOverride,
            TraceId = request.TraceId,
            PrincipalId = request.PrincipalId,
            TaskId = request.TaskId,
            SessionId = request.SessionId
        };

        request.ExecutionState = ExecutionState.Dispatched;
        request.UpdatedAt = DateTime.UtcNow;
        UpdateState(request);

        _auditService.RecordEvent(request.TraceId, "EXECUTION_DISPATCHED",
            request.PrincipalId, request.TaskId, request.SessionId, request.CapabilityId);

        ExecutionResult executionResult;
        try
        {
            executionResult = await _executionDispatcher.DispatchAsync(approvedRequest);
        }
        catch (Exception ex)
        {
            executionResult = ExecutionResult.Fail(request.RequestId, ex.Message);
        }

        if (executionResult.Success)
        {
            request.ExecutionState = ExecutionState.Succeeded;
            request.ResultPayload = executionResult.ResultPayload;
            request.EvidenceRef = executionResult.EvidenceRef;
            _auditService.RecordEvent(request.TraceId, "EXECUTION_SUCCEEDED",
                request.PrincipalId, request.TaskId, request.SessionId, request.CapabilityId);
        }
        else
        {
            request.ExecutionState = ExecutionState.Failed;
            request.ResultPayload = JsonSerializer.Serialize(new { error = executionResult.ErrorMessage });
            _auditService.RecordEvent(request.TraceId, "EXECUTION_FAILED",
                request.PrincipalId, request.TaskId, request.SessionId, request.CapabilityId,
                JsonSerializer.Serialize(new { error = executionResult.ErrorMessage }));
        }

        request.UpdatedAt = DateTime.UtcNow;
        UpdateState(request);
        return request;
    }

    /// <inheritdoc />
    public IReadOnlyList<ApprovalRequest> ListPendingApprovals()
    {
        return _db.Query<ApprovalRequest>(
            "SELECT * FROM approval_requests WHERE status = @s ORDER BY created_at",
            new { s = (int)ApprovalStatus.Pending });
    }

    /// <inheritdoc />
    public IReadOnlyList<ApprovalRequest> ListPendingApprovalsForApprover(string approverId, bool isAdmin)
    {
        var pending = ListPendingApprovals();
        if (isAdmin) return pending; // 管理員看全部
        // 使用者只看自己擁有的 User 層
        return pending
            .Where(a => a.ApproverTier == ApproverTier.User
                && string.Equals(a.OwnerPrincipalId, approverId, StringComparison.Ordinal))
            .ToList();
    }

    /// <inheritdoc />
    public ApprovalDetail? GetApprovalDetail(string approvalId)
    {
        var approval = _db.Get<ApprovalRequest>(approvalId);
        return approval == null ? null : BuildApprovalDetail(approval);
    }

    /// <inheritdoc />
    public IReadOnlyList<ApprovalDetail> ListPendingApprovalDetailsForApprover(string approverId, bool isAdmin)
    {
        return ListPendingApprovalsForApprover(approverId, isAdmin)
            .Select(BuildApprovalDetail)
            .ToList();
    }

    private ApprovalDetail BuildApprovalDetail(ApprovalRequest a)
    {
        var req = _db.Get<ExecutionRequest>(a.RequestId);
        return new ApprovalDetail
        {
            ApprovalId = a.ApprovalId,
            RequestId = a.RequestId,
            CapabilityId = a.CapabilityId,
            Tier = a.ApproverTier.ToString(),
            OwnerPrincipalId = a.OwnerPrincipalId,
            Reason = a.Reason,
            Intent = req?.Intent ?? string.Empty,
            CreatedAt = a.CreatedAt,
            ExpiresAt = a.ExpiresAt,
            Rendered = BuildRendered(a.CapabilityId, req?.RequestPayload ?? "{}")
        };
    }

    /// <summary>依能力把請求 payload 渲染成可視內容(§18.2-C2 賣點)。</summary>
    private static RenderedContent BuildRendered(string capabilityId, string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var args = root.TryGetProperty("args", out var a) ? a : root;
            string? S(string n) =>
                args.ValueKind == JsonValueKind.Object
                && args.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;

            switch (capabilityId)
            {
                case "repo.patch.apply":
                    return new RenderedContent { Kind = "patch", Patch = S("patch") ?? string.Empty };
                case "build.test.run":
                    return new RenderedContent { Kind = "command", Command = S("command") ?? string.Empty };
                case "file.write":
                    var content = S("content") ?? string.Empty;
                    return new RenderedContent
                    {
                        Kind = "file",
                        Path = S("path") ?? string.Empty,
                        ContentPreview = content.Length > 2000 ? content[..2000] + "…" : content
                    };
                default:
                    return new RenderedContent
                    {
                        Kind = "json",
                        Payload = args.ValueKind == JsonValueKind.Undefined ? payload : args.GetRawText()
                    };
            }
        }
        catch
        {
            return new RenderedContent { Kind = "json", Payload = payload };
        }
    }

    /// <summary>
    /// 授權檢查(§18.2):管理員可批任何待審;非管理員只能批 User 層、且是自己擁有的。
    /// </summary>
    private static bool IsAuthorizedApprover(ApprovalRequest approval, string approverId, bool isAdmin)
    {
        if (isAdmin) return true;
        return approval.ApproverTier == ApproverTier.User
            && string.Equals(approverId, approval.OwnerPrincipalId, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<ExecutionRequest?> ApproveExecutionAsync(string approvalId, string approverId, string reason, bool isAdmin = false)
    {
        var approval = _db.Get<ApprovalRequest>(approvalId);
        if (approval == null || approval.Status != ApprovalStatus.Pending)
            return null;

        if (!IsAuthorizedApprover(approval, approverId, isAdmin))
            return null;

        var request = _db.Get<ExecutionRequest>(approval.RequestId);
        if (request == null)
            return null;

        if (approval.ExpiresAt < DateTime.UtcNow)
        {
            approval.Status = ApprovalStatus.Expired;
            approval.DecidedAt = DateTime.UtcNow;
            _db.Update(approval);
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, "Approval expired before decision.");
        }

        var capability = _capabilityCatalog.GetCapability(approval.CapabilityId);
        var grant = _capabilityCatalog.GetActiveGrant(
            approval.PrincipalId, approval.TaskId, approval.SessionId, approval.CapabilityId);
        if (capability == null || grant == null)
        {
            approval.Status = ApprovalStatus.Rejected;
            approval.DecidedAt = DateTime.UtcNow;
            approval.DecisionReason = "Capability or grant unavailable at approval time.";
            _db.Update(approval);
            return UpdateRequestState(request, ExecutionState.Denied,
                Models.PolicyDecision.Deny, "Capability or grant no longer available at approval time.");
        }

        approval.Status = ApprovalStatus.Approved;
        approval.ApproverId = approverId;
        approval.DecisionReason = reason;
        approval.DecidedAt = DateTime.UtcNow;
        _db.Update(approval);

        request.ExecutionState = ExecutionState.Allowed;
        request.PolicyDecision = Models.PolicyDecision.Allow;
        request.PolicyReason = $"Approved by {approverId}: {reason}";
        request.UpdatedAt = DateTime.UtcNow;
        UpdateState(request);

        _auditService.RecordEvent(approval.TraceId, "EXECUTION_APPROVED",
            approverId, approval.TaskId, approval.SessionId, approval.CapabilityId,
            JsonSerializer.Serialize(new { approval_id = approvalId, reason }));

        return await DispatchAndRecordAsync(request, capability, grant);
    }

    /// <inheritdoc />
    public ExecutionRequest? RejectExecution(string approvalId, string approverId, string reason, bool isAdmin = false)
    {
        var approval = _db.Get<ApprovalRequest>(approvalId);
        if (approval == null || approval.Status != ApprovalStatus.Pending)
            return null;

        if (!IsAuthorizedApprover(approval, approverId, isAdmin))
            return null;

        approval.Status = ApprovalStatus.Rejected;
        approval.ApproverId = approverId;
        approval.DecisionReason = reason;
        approval.DecidedAt = DateTime.UtcNow;
        _db.Update(approval);

        var request = _db.Get<ExecutionRequest>(approval.RequestId);
        if (request == null)
            return null;

        _auditService.RecordEvent(approval.TraceId, "EXECUTION_REJECTED",
            approverId, approval.TaskId, approval.SessionId, approval.CapabilityId,
            JsonSerializer.Serialize(new { approval_id = approvalId, reason }));

        return UpdateRequestState(request, ExecutionState.Denied,
            Models.PolicyDecision.Deny, $"Rejected by {approverId}: {reason}");
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
