using System.Text.Json;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 因果工作流引擎 —— DAG 排程 + 逐節點 PEP 裁決 + 執行
///
/// Phase 4：循序執行（一次一個就緒節點），Phase 5 再支援同級並行。
/// 每個節點透過 BrokerService.SubmitExecutionRequest() 走完整 PEP 16 步裁決。
/// DataFlow 邊透過 SharedContext 傳遞節點間資料。
/// </summary>
public class PlanEngine : IPlanEngine
{
    private readonly IPlanService _planService;
    private readonly IBrokerService _brokerService;
    private readonly ISharedContextService _contextService;
    private readonly IAuditService _auditService;
    private readonly IObservationService? _observationService;

    public PlanEngine(
        IPlanService planService,
        IBrokerService brokerService,
        ISharedContextService contextService,
        IAuditService auditService,
        IObservationService? observationService = null)
    {
        _planService = planService;
        _brokerService = brokerService;
        _contextService = contextService;
        _auditService = auditService;
        _observationService = observationService;
    }

    public async Task<Plan> SubmitAndExecuteAsync(string planId, string principalId,
                                                    string sessionId, string traceId,
                                                    CancellationToken cancellationToken = default)
    {
        var plan = _planService.GetPlan(planId)
            ?? throw new InvalidOperationException($"Plan '{planId}' not found.");

        if (plan.State != PlanState.Draft && plan.State != PlanState.Submitted)
            throw new InvalidOperationException(
                $"Plan '{planId}' is in state '{plan.State}', expected Draft or Submitted.");

        // Step 1: 驗證 DAG
        var (isValid, error) = _planService.ValidateDag(planId);
        if (!isValid)
            throw new InvalidOperationException($"DAG validation failed: {error}");

        // Step 2: Plan → Submitted → Running
        _planService.UpdatePlanState(planId, PlanState.Submitted);
        _planService.UpdatePlanState(planId, PlanState.Running);

        _auditService.RecordEvent(
            traceId: traceId,
            eventType: "PLAN_STARTED",
            principalId: principalId,
            taskId: plan.TaskId,
            details: JsonSerializer.Serialize(new { planId, totalNodes = plan.TotalNodes }));

        EmitObservation("PLAN_STARTED", traceId, ObservationSeverity.Info,
            observedState: JsonSerializer.Serialize(new { state = PlanState.Running.ToString(), totalNodes = plan.TotalNodes }),
            planId: planId, principalId: principalId);

        // Step 3: 拓撲序迴圈（M-8 修復：支援 CancellationToken 取消）
        int maxIterations = plan.TotalNodes * 3; // 防無限迴圈（含重試）
        int iteration = 0;

        try
        {
            while (iteration < maxIterations)
            {
                iteration++;

                // M-8 修復：每次迴圈迭代檢查取消
                cancellationToken.ThrowIfCancellationRequested();

                var readyNodes = _planService.GetReadyNodes(planId);

                if (readyNodes.Count == 0)
                {
                    // 檢查是否全部完成
                    var allNodes = _planService.GetNodes(planId);
                    var pendingOrRunning = allNodes.FindAll(n =>
                        n.State == NodeState.Pending || n.State == NodeState.Running ||
                        n.State == NodeState.Ready);

                    if (pendingOrRunning.Count == 0)
                    {
                        // 全部完成（Succeeded / Failed / Skipped / Cancelled）
                        var failedNodes = allNodes.FindAll(n => n.State == NodeState.Failed);
                        if (failedNodes.Count > 0)
                        {
                            _planService.UpdatePlanState(planId, PlanState.Failed);
                            _auditService.RecordEvent(
                                traceId: traceId,
                                eventType: "PLAN_FAILED",
                                principalId: principalId,
                                taskId: plan.TaskId,
                                details: JsonSerializer.Serialize(new
                                {
                                    planId,
                                    failedNodeCount = failedNodes.Count,
                                    failedNodeIds = failedNodes.ConvertAll(n => n.NodeId)
                                }));
                            EmitObservation("PLAN_FAILED", traceId, ObservationSeverity.Alert,
                                observedState: JsonSerializer.Serialize(new { state = PlanState.Failed.ToString(), failedNodeCount = failedNodes.Count }),
                                expectedState: JsonSerializer.Serialize(new { state = PlanState.Completed.ToString() }),
                                planId: planId, principalId: principalId);
                        }
                        else
                        {
                            _planService.UpdatePlanState(planId, PlanState.Completed);
                            _auditService.RecordEvent(
                                traceId: traceId,
                                eventType: "PLAN_COMPLETED",
                                principalId: principalId,
                                taskId: plan.TaskId,
                                details: JsonSerializer.Serialize(new { planId }));
                            EmitObservation("PLAN_COMPLETED", traceId, ObservationSeverity.Info,
                                observedState: JsonSerializer.Serialize(new { state = PlanState.Completed.ToString() }),
                                expectedState: JsonSerializer.Serialize(new { state = PlanState.Completed.ToString() }),
                                planId: planId, principalId: principalId);
                        }
                        break;
                    }
                    else
                    {
                        // Deadlock：有未完成節點但無就緒節點
                        _planService.UpdatePlanState(planId, PlanState.Failed);
                        _auditService.RecordEvent(
                            traceId: traceId,
                            eventType: "PLAN_DEADLOCKED",
                            principalId: principalId,
                            taskId: plan.TaskId,
                            details: JsonSerializer.Serialize(new
                            {
                                planId,
                                blockedNodeCount = pendingOrRunning.Count,
                                blockedNodeIds = pendingOrRunning.ConvertAll(n => n.NodeId)
                            }));
                        break;
                    }
                }

                // Phase 4：循序處理就緒節點（一次一個）
                foreach (var node in readyNodes)
                {
                    // M-8 修復：每個節點執行前檢查取消
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteNodeAsync(node, plan, principalId, sessionId, traceId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // M-8 修復：請求取消 → Plan 標記為 Cancelled
            _planService.UpdatePlanState(planId, PlanState.Cancelled);
            _auditService.RecordEvent(
                traceId: traceId,
                eventType: "PLAN_CANCELLED",
                principalId: principalId,
                taskId: plan.TaskId,
                details: JsonSerializer.Serialize(new { planId, reason = "Cancellation requested" }));
            EmitObservation("PLAN_CANCELLED", traceId, ObservationSeverity.Warning,
                observedState: JsonSerializer.Serialize(new { state = PlanState.Cancelled.ToString() }),
                planId: planId, principalId: principalId);
            throw; // 重新拋出讓呼叫者知道已取消
        }

        // 重新讀取最終狀態
        return _planService.GetPlan(planId)!;
    }

    /// <summary>執行單一節點：DataFlow 注入 → PEP 裁決 → 結果寫出 → Checkpoint</summary>
    private async Task ExecuteNodeAsync(PlanNode node, Plan plan, string principalId,
                                         string sessionId, string traceId)
    {
        // Mark running
        _planService.UpdateNodeState(node.NodeId, NodeState.Running, null);

        _auditService.RecordEvent(
            traceId: traceId,
            eventType: "NODE_DISPATCHED",
            principalId: principalId,
            taskId: plan.TaskId,
            details: JsonSerializer.Serialize(new
            {
                planId = plan.PlanId,
                nodeId = node.NodeId,
                capabilityId = node.CapabilityId,
                intent = node.Intent
            }));

        EmitObservation("NODE_DISPATCHED", traceId, ObservationSeverity.Info,
            observedState: JsonSerializer.Serialize(new { state = NodeState.Running.ToString(), capabilityId = node.CapabilityId }),
            planId: plan.PlanId, nodeId: node.NodeId, principalId: principalId);

        try
        {
            // Step A: DataFlow 入邊 → 注入 upstream context 到 payload
            var requestPayload = InjectDataFlowContext(node, principalId);

            // Step B: 透過 BrokerService PEP 16 步裁決 + 執行（H-3：proper async）
            var idempotencyKey = $"plan_{plan.PlanId}_node_{node.NodeId}_attempt_{node.RetryCount}";
            var execRequest = await _brokerService.SubmitExecutionRequestAsync(
                principalId,
                plan.TaskId,
                sessionId,
                node.CapabilityId,
                node.Intent,
                requestPayload,
                idempotencyKey,
                traceId);

            // 關聯 request_id
            _planService.UpdateNodeState(node.NodeId, NodeState.Running, execRequest.RequestId);

            // Step C: 檢查執行結果
            if (execRequest.ExecutionState == ExecutionState.Succeeded)
            {
                _planService.UpdateNodeState(node.NodeId, NodeState.Succeeded, execRequest.RequestId);

                // 寫出 node output 到 SharedContext
                if (!string.IsNullOrEmpty(node.OutputContextKey))
                {
                    var outputPayload = execRequest.ResultPayload ?? "{}";
                    _contextService.Write(
                        authorPrincipalId: principalId,
                        documentId: $"node_output_{node.NodeId}",
                        key: node.OutputContextKey,
                        contentRef: outputPayload,
                        contentType: "application/json",
                        acl: "{\"read\":[\"*\"]}",  // plan 內所有角色可讀
                        taskId: plan.TaskId);
                }

                // 建立 Checkpoint
                _planService.CreateCheckpoint(plan.PlanId, node.NodeId,
                    JsonSerializer.Serialize(new
                    {
                        nodeState = NodeState.Succeeded.ToString(),
                        requestId = execRequest.RequestId,
                        resultPayload = execRequest.ResultPayload
                    }));

                _auditService.RecordEvent(
                    traceId: traceId,
                    eventType: "NODE_COMPLETED",
                    principalId: principalId,
                    taskId: plan.TaskId,
                    details: JsonSerializer.Serialize(new
                    {
                        planId = plan.PlanId,
                        nodeId = node.NodeId,
                        state = "Succeeded",
                        requestId = execRequest.RequestId
                    }));

                EmitObservation("NODE_COMPLETED", traceId, ObservationSeverity.Info,
                    observedState: JsonSerializer.Serialize(new { state = NodeState.Succeeded.ToString() }),
                    expectedState: JsonSerializer.Serialize(new { state = NodeState.Succeeded.ToString() }),
                    planId: plan.PlanId, nodeId: node.NodeId, requestId: execRequest.RequestId, principalId: principalId);
            }
            else if (execRequest.ExecutionState == ExecutionState.Denied)
            {
                // PEP 拒絕 → 節點直接失敗（不重試）
                _planService.UpdateNodeState(node.NodeId, NodeState.Failed, execRequest.RequestId);
                HandleNodeFailure(node, plan, principalId, traceId,
                    $"PEP denied: {execRequest.PolicyReason}");
            }
            else if (execRequest.ExecutionState == ExecutionState.Failed)
            {
                HandleNodeExecutionFailure(node, plan, principalId, sessionId, traceId, execRequest);
            }
            else
            {
                // 其他中間狀態（Received, Validated, Allowed, Dispatched）→ 視為失敗
                _planService.UpdateNodeState(node.NodeId, NodeState.Failed, execRequest.RequestId);
                HandleNodeFailure(node, plan, principalId, traceId,
                    $"Unexpected execution state: {execRequest.ExecutionState}");
            }
        }
        catch (Exception ex)
        {
            _planService.UpdateNodeState(node.NodeId, NodeState.Failed, null);
            HandleNodeFailure(node, plan, principalId, traceId, $"Exception: {ex.Message}");
        }
    }

    /// <summary>處理節點執行失敗（含重試邏輯）</summary>
    private void HandleNodeExecutionFailure(PlanNode node, Plan plan,
                                             string principalId, string sessionId,
                                             string traceId, ExecutionRequest execRequest)
    {
        if (node.RetryCount < node.MaxRetries - 1)
        {
            // 可重試
            _planService.IncrementRetryCount(node.NodeId);
            _planService.UpdateNodeState(node.NodeId, NodeState.Pending, null); // 回到 Pending 等下一輪

            _auditService.RecordEvent(
                traceId: traceId,
                eventType: "NODE_RETRY",
                principalId: principalId,
                taskId: plan.TaskId,
                details: JsonSerializer.Serialize(new
                {
                    planId = plan.PlanId,
                    nodeId = node.NodeId,
                    retryCount = node.RetryCount + 1,
                    maxRetries = node.MaxRetries,
                    reason = execRequest.ResultPayload
                }));
        }
        else
        {
            // 已用盡重試
            _planService.UpdateNodeState(node.NodeId, NodeState.Failed, execRequest.RequestId);
            HandleNodeFailure(node, plan, principalId, traceId,
                $"Execution failed after {node.MaxRetries} attempt(s): {execRequest.ResultPayload}");
        }
    }

    /// <summary>節點最終失敗 → 取消下游待處理節點</summary>
    private void HandleNodeFailure(PlanNode node, Plan plan,
                                    string principalId, string traceId, string reason)
    {
        _auditService.RecordEvent(
            traceId: traceId,
            eventType: "NODE_FAILED",
            principalId: principalId,
            taskId: plan.TaskId,
            details: JsonSerializer.Serialize(new
            {
                planId = plan.PlanId,
                nodeId = node.NodeId,
                reason
            }));

        // 級聯取消下游 Pending 節點
        CancelDownstreamNodes(node.NodeId, plan.PlanId, traceId, principalId, plan.TaskId);
    }

    /// <summary>級聯取消失敗節點的所有下游 Pending 節點</summary>
    private void CancelDownstreamNodes(string failedNodeId, string planId,
                                        string traceId, string principalId, string taskId)
    {
        var edges = _planService.GetEdges(planId);
        var nodes = _planService.GetNodes(planId);

        // BFS 找所有下游節點
        var toCancel = new Queue<string>();
        var cancelled = new HashSet<string>();

        foreach (var edge in edges.FindAll(e => e.FromNodeId == failedNodeId))
            toCancel.Enqueue(edge.ToNodeId);

        while (toCancel.Count > 0)
        {
            var nodeId = toCancel.Dequeue();
            if (cancelled.Contains(nodeId)) continue;
            cancelled.Add(nodeId);

            var node = nodes.Find(n => n.NodeId == nodeId);
            if (node != null && node.State == NodeState.Pending)
            {
                _planService.UpdateNodeState(nodeId, NodeState.Cancelled, null);

                _auditService.RecordEvent(
                    traceId: traceId,
                    eventType: "NODE_CANCELLED",
                    principalId: principalId,
                    taskId: taskId,
                    details: JsonSerializer.Serialize(new
                    {
                        planId,
                        nodeId,
                        reason = $"Upstream node '{failedNodeId}' failed"
                    }));

                // 繼續往下游傳播
                foreach (var edge in edges.FindAll(e => e.FromNodeId == nodeId))
                    toCancel.Enqueue(edge.ToNodeId);
            }
        }
    }

    /// <summary>
    /// DataFlow 入邊：從 SharedContext 讀取上游 node output，注入到 request payload
    /// </summary>
    private string InjectDataFlowContext(PlanNode node, string principalId)
    {
        var incomingEdges = _planService.GetIncomingEdges(node.NodeId);
        var dataFlowEdges = incomingEdges.FindAll(e => e.EdgeType == EdgeType.DataFlow);

        if (dataFlowEdges.Count == 0)
            return node.RequestPayload;

        // 讀取所有上游 context
        var upstreamData = new Dictionary<string, string>();
        foreach (var edge in dataFlowEdges)
        {
            if (string.IsNullOrEmpty(edge.ContextKey)) continue;

            var plan = _planService.GetPlan(node.PlanId);
            var contextEntry = _contextService.ReadByKey(edge.ContextKey, plan?.TaskId, principalId);
            if (contextEntry != null)
            {
                upstreamData[edge.ContextKey] = contextEntry.ContentRef;
            }
        }

        if (upstreamData.Count == 0)
            return node.RequestPayload;

        // 將上游資料注入到 payload 的 "_upstream" 欄位
        try
        {
            using var doc = JsonDocument.Parse(node.RequestPayload);
            var payload = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                payload[prop.Name] = prop.Value.Clone();
            }

            // 加入 _upstream 欄位（M-12 修復：using 防止 JsonDocument 洩漏）
            var upstreamJson = JsonSerializer.Serialize(upstreamData);
            using var upstreamDoc = JsonDocument.Parse(upstreamJson);
            payload["_upstream"] = upstreamDoc.RootElement.Clone();

            return JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            // M-4 修復：記錄異常而非靜默吞掉
            _auditService.RecordEvent(
                traceId: node.PlanId,
                eventType: "DATAFLOW_INJECT_ERROR",
                principalId: principalId,
                taskId: node.PlanId,
                details: JsonSerializer.Serialize(new { nodeId = node.NodeId, error = ex.Message }));
            return node.RequestPayload;
        }
    }

    // ── 觀測點 ──

    /// <summary>發射 Internal 觀測事件（若 ObservationService 已注入）</summary>
    private void EmitObservation(
        string eventType,
        string traceId,
        ObservationSeverity severity,
        string observedState,
        string? expectedState = null,
        string? planId = null,
        string? nodeId = null,
        string? requestId = null,
        string? principalId = null,
        string details = "{}")
    {
        _observationService?.Record(new ObservationEvent
        {
            ObservationId = IdGen.New("obs"),
            Source = ObservationSource.Internal,
            EventType = eventType,
            PlanId = planId,
            NodeId = nodeId,
            RequestId = requestId,
            TraceId = traceId,
            PrincipalId = principalId,
            ObservedState = observedState,
            ExpectedState = expectedState,
            Severity = severity,
            Details = details,
            ObservedAt = DateTime.UtcNow
        });
    }
}
