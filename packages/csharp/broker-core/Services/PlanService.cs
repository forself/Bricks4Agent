using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 計畫服務 —— Plan/Node/Edge CRUD + DAG 驗證（Kahn's algorithm）
///
/// 所有操作產生稽核事件，複用 BrokerDb + IdGen。
/// </summary>
public class PlanService : IPlanService
{
    private readonly BrokerDb _db;
    private readonly IAuditService _auditService;

    public PlanService(BrokerDb db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    // ── Plan CRUD ──

    public Plan CreatePlan(string taskId, string submittedBy, string title, string? description)
    {
        var plan = new Plan
        {
            PlanId = IdGen.New("plan"),
            TaskId = taskId,
            SubmittedBy = submittedBy,
            Title = title,
            Description = description,
            State = PlanState.Draft,
            TotalNodes = 0,
            CompletedNodes = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Insert(plan);

        _auditService.RecordEvent(
            traceId: plan.PlanId,
            eventType: "PLAN_CREATED",
            principalId: submittedBy,
            taskId: taskId,
            details: JsonSerializer.Serialize(new { title }));

        return plan;
    }

    public Plan? GetPlan(string planId)
    {
        return _db.Get<Plan>(planId);
    }

    public bool UpdatePlanState(string planId, PlanState newState)
    {
        var affected = _db.Execute(
            "UPDATE plans SET state = @state, updated_at = @now WHERE plan_id = @pid",
            new { state = (int)newState, now = DateTime.UtcNow, pid = planId });

        if (affected > 0)
        {
            _auditService.RecordEvent(
                traceId: planId,
                eventType: "PLAN_STATE_CHANGED",
                details: JsonSerializer.Serialize(new { newState = newState.ToString() }));
        }

        return affected > 0;
    }

    // ── Node 管理 ──

    public PlanNode AddNode(string planId, string capabilityId, string intent,
                            string requestPayload, string? outputContextKey, int maxRetries)
    {
        var plan = _db.Get<Plan>(planId);
        if (plan == null)
            throw new InvalidOperationException($"Plan '{planId}' not found.");

        if (plan.State != PlanState.Draft)
            throw new InvalidOperationException($"Cannot add nodes to plan in state '{plan.State}'.");

        var node = new PlanNode
        {
            NodeId = IdGen.New("node"),
            PlanId = planId,
            Ordinal = 0, // ValidateDag 計算
            CapabilityId = capabilityId,
            Intent = intent,
            RequestPayload = requestPayload,
            State = NodeState.Pending,
            OutputContextKey = outputContextKey,
            RetryCount = 0,
            MaxRetries = maxRetries > 0 ? maxRetries : 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Insert(node);

        // 更新 plan.total_nodes
        _db.Execute(
            "UPDATE plans SET total_nodes = total_nodes + 1, updated_at = @now WHERE plan_id = @pid",
            new { now = DateTime.UtcNow, pid = planId });

        _auditService.RecordEvent(
            traceId: planId,
            eventType: "NODE_ADDED",
            details: JsonSerializer.Serialize(new
            {
                nodeId = node.NodeId,
                capabilityId,
                intent,
                outputContextKey
            }));

        return node;
    }

    public List<PlanNode> GetNodes(string planId)
    {
        return _db.Query<PlanNode>(
            "SELECT * FROM plan_nodes WHERE plan_id = @pid ORDER BY ordinal, created_at",
            new { pid = planId });
    }

    public bool UpdateNodeState(string nodeId, NodeState state, string? requestId)
    {
        var affected = _db.Execute(
            "UPDATE plan_nodes SET state = @state, request_id = @reqId, updated_at = @now WHERE node_id = @nid",
            new { state = (int)state, reqId = requestId, now = DateTime.UtcNow, nid = nodeId });

        if (affected > 0)
        {
            var node = _db.Get<PlanNode>(nodeId);
            if (node != null)
            {
                _auditService.RecordEvent(
                    traceId: node.PlanId,
                    eventType: "NODE_STATE_CHANGED",
                    details: JsonSerializer.Serialize(new
                    {
                        nodeId,
                        newState = state.ToString(),
                        requestId
                    }));

                // 若 Succeeded → 更新 plan.completed_nodes
                if (state == NodeState.Succeeded)
                {
                    _db.Execute(
                        "UPDATE plans SET completed_nodes = completed_nodes + 1, updated_at = @now WHERE plan_id = @pid",
                        new { now = DateTime.UtcNow, pid = node.PlanId });
                }
            }
        }

        return affected > 0;
    }

    public bool IncrementRetryCount(string nodeId)
    {
        var affected = _db.Execute(
            "UPDATE plan_nodes SET retry_count = retry_count + 1, updated_at = @now WHERE node_id = @nid",
            new { now = DateTime.UtcNow, nid = nodeId });
        return affected > 0;
    }

    // ── Edge 管理 ──

    public PlanEdge AddEdge(string planId, string fromNodeId, string toNodeId,
                            EdgeType edgeType, string? contextKey, string? condition)
    {
        var plan = _db.Get<Plan>(planId);
        if (plan == null)
            throw new InvalidOperationException($"Plan '{planId}' not found.");

        if (plan.State != PlanState.Draft)
            throw new InvalidOperationException($"Cannot add edges to plan in state '{plan.State}'.");

        // 驗證 from/to 節點存在且屬於此計畫
        var fromNode = _db.Get<PlanNode>(fromNodeId);
        var toNode = _db.Get<PlanNode>(toNodeId);

        if (fromNode == null || fromNode.PlanId != planId)
            throw new InvalidOperationException($"From node '{fromNodeId}' not found in plan '{planId}'.");
        if (toNode == null || toNode.PlanId != planId)
            throw new InvalidOperationException($"To node '{toNodeId}' not found in plan '{planId}'.");
        if (fromNodeId == toNodeId)
            throw new InvalidOperationException("Self-loop edge is not allowed.");

        var edge = new PlanEdge
        {
            EdgeId = IdGen.New("edge"),
            PlanId = planId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            EdgeType = edgeType,
            ContextKey = contextKey,
            Condition = condition
        };

        _db.Insert(edge);

        _auditService.RecordEvent(
            traceId: planId,
            eventType: "EDGE_ADDED",
            details: JsonSerializer.Serialize(new
            {
                edgeId = edge.EdgeId,
                fromNodeId,
                toNodeId,
                edgeType = edgeType.ToString(),
                contextKey
            }));

        return edge;
    }

    public List<PlanEdge> GetEdges(string planId)
    {
        return _db.Query<PlanEdge>(
            "SELECT * FROM plan_edges WHERE plan_id = @pid",
            new { pid = planId });
    }

    public List<PlanEdge> GetIncomingEdges(string nodeId)
    {
        return _db.Query<PlanEdge>(
            "SELECT * FROM plan_edges WHERE to_node_id = @nid",
            new { nid = nodeId });
    }

    // ── DAG 驗證（Kahn's Algorithm） ──

    public (bool IsValid, string? Error) ValidateDag(string planId)
    {
        var nodes = GetNodes(planId);
        var edges = GetEdges(planId);

        if (nodes.Count == 0)
            return (false, "Plan has no nodes.");

        // 建立鄰接表 + 入度計數
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var node in nodes)
        {
            inDegree[node.NodeId] = 0;
            adjacency[node.NodeId] = new List<string>();
        }

        foreach (var edge in edges)
        {
            if (!inDegree.ContainsKey(edge.FromNodeId))
                return (false, $"Edge references unknown from_node '{edge.FromNodeId}'.");
            if (!inDegree.ContainsKey(edge.ToNodeId))
                return (false, $"Edge references unknown to_node '{edge.ToNodeId}'.");

            adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            inDegree[edge.ToNodeId]++;
        }

        // Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (nodeId, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(nodeId);
        }

        int ordinal = 0;
        var sortedNodeIds = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sortedNodeIds.Add(current);

            // 設定拓撲序號
            _db.Execute(
                "UPDATE plan_nodes SET ordinal = @ord, updated_at = @now WHERE node_id = @nid",
                new { ord = ordinal, now = DateTime.UtcNow, nid = current });
            ordinal++;

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (sortedNodeIds.Count != nodes.Count)
        {
            return (false, $"Cycle detected: {nodes.Count - sortedNodeIds.Count} node(s) form a cycle.");
        }

        _auditService.RecordEvent(
            traceId: planId,
            eventType: "DAG_VALIDATED",
            details: JsonSerializer.Serialize(new
            {
                nodeCount = nodes.Count,
                edgeCount = edges.Count,
                topologicalOrder = sortedNodeIds
            }));

        return (true, null);
    }

    // ── 就緒查詢 ──

    public List<PlanNode> GetReadyNodes(string planId)
    {
        // M-11 修復：批次載入，消除 N+1 查詢
        // 一次取出 plan 下所有節點 + 所有邊，在記憶體內做 ready 判斷
        var allNodes = _db.Query<PlanNode>(
            "SELECT * FROM plan_nodes WHERE plan_id = @pid",
            new { pid = planId });
        var allEdges = _db.Query<PlanEdge>(
            "SELECT * FROM plan_edges WHERE plan_id = @pid",
            new { pid = planId });

        // 建立 nodeId → state 快查表
        var nodeStateMap = new Dictionary<string, NodeState>(allNodes.Count);
        foreach (var n in allNodes)
            nodeStateMap[n.NodeId] = n.State;

        // 建立 toNodeId → List<fromNodeId> 入邊索引
        var incomingMap = new Dictionary<string, List<string>>();
        foreach (var edge in allEdges)
        {
            if (!incomingMap.TryGetValue(edge.ToNodeId, out var list))
            {
                list = new List<string>();
                incomingMap[edge.ToNodeId] = list;
            }
            list.Add(edge.FromNodeId);
        }

        var readyNodes = new List<PlanNode>();

        foreach (var node in allNodes)
        {
            if (node.State != NodeState.Pending)
                continue;

            if (!incomingMap.TryGetValue(node.NodeId, out var predecessorIds) ||
                predecessorIds.Count == 0)
            {
                // 無入邊 → 直接就緒
                readyNodes.Add(node);
                continue;
            }

            // 所有入邊的 fromNode 都已 Succeeded（記憶體查找，零 DB 查詢）
            bool allPredecessorsSucceeded = true;
            foreach (var fromId in predecessorIds)
            {
                if (!nodeStateMap.TryGetValue(fromId, out var fromState) ||
                    fromState != NodeState.Succeeded)
                {
                    allPredecessorsSucceeded = false;
                    break;
                }
            }

            if (allPredecessorsSucceeded)
                readyNodes.Add(node);
        }

        // 按拓撲序排列
        readyNodes.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
        return readyNodes;
    }

    // ── Checkpoint ──

    public Checkpoint CreateCheckpoint(string planId, string nodeId, string snapshotRef)
    {
        var checkpoint = new Checkpoint
        {
            CheckpointId = IdGen.New("ckpt"),
            PlanId = planId,
            NodeId = nodeId,
            State = CheckpointState.Captured,
            SnapshotRef = snapshotRef,
            CreatedAt = DateTime.UtcNow
        };

        _db.Insert(checkpoint);

        _auditService.RecordEvent(
            traceId: planId,
            eventType: "CHECKPOINT_CREATED",
            details: JsonSerializer.Serialize(new { checkpointId = checkpoint.CheckpointId, nodeId }));

        return checkpoint;
    }

    public List<Checkpoint> GetCheckpoints(string planId)
    {
        return _db.Query<Checkpoint>(
            "SELECT * FROM checkpoints WHERE plan_id = @pid ORDER BY created_at",
            new { pid = planId });
    }
}
