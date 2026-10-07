using Broker.Helpers;
using Broker.Middleware;
using BrokerCore;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/plans/* — 因果工作流（Plan/Node/Edge/DAG）端點</summary>
public static class PlanEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var plans = group.MapGroup("/plans");

        // ── 建立計畫 ──
        plans.MapPost("/create", (HttpContext ctx, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            // M-1 修復：驗證必填欄位
            if (!RequestBodyHelper.TryGetRequiredFields(body,
                new[] { "task_id", "title" }, out var fields, out var err))
                return err!;
            var taskId = fields["task_id"];
            var title = fields["title"];
            var description = body.TryGetProperty("description", out var d)
                ? d.GetString() : null;

            if (!BrokerAuthorization.TryRequireTaskAccess(ctx, broker, taskId, out var denied))
                return denied;

            var plan = planService.CreatePlan(taskId, principalId, title, description);
            return Results.Ok(ApiResponseHelper.Success(plan));
        });

        // ── 查詢計畫 ──
        plans.MapPost("/get", (HttpContext ctx, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "plan_id", out var planId, out var err))
                return err!;

            var plan = planService.GetPlan(planId);
            if (plan == null)
                return Results.NotFound(ApiResponseHelper.Error("Plan not found.", 404));

            if (!TryRequirePlanAccess(ctx, plan, broker, out var denied))
                return denied;

            return Results.Ok(ApiResponseHelper.Success(ForCaller(ctx, plan)));
        });

        // ── 新增節點 ──
        plans.MapPost("/add-node", (HttpContext ctx, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            // M-1 修復：驗證必填欄位
            if (!RequestBodyHelper.TryGetRequiredFields(body,
                new[] { "plan_id", "capability_id", "intent" }, out var fields, out var err))
                return err!;
            var planId = fields["plan_id"];
            var capabilityId = fields["capability_id"];
            var intent = fields["intent"];
            var requestPayload = body.TryGetProperty("request_payload", out var rp)
                ? rp.GetRawText() : "{}";
            var outputContextKey = body.TryGetProperty("output_context_key", out var ock)
                ? ock.GetString() : null;
            var maxRetries = body.TryGetProperty("max_retries", out var mr)
                ? mr.GetInt32() : 1;

            if (!TryRequirePlanAccess(ctx, planService.GetPlan(planId), broker, out var denied))
                return denied;

            try
            {
                var node = planService.AddNode(planId, capabilityId, intent,
                    requestPayload, outputContextKey, maxRetries);
                return Results.Ok(ApiResponseHelper.Success(node));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── 新增邊 ──
        plans.MapPost("/add-edge", (HttpContext ctx, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            // M-1 修復：驗證必填欄位
            if (!RequestBodyHelper.TryGetRequiredFields(body,
                new[] { "plan_id", "from_node_id", "to_node_id" }, out var fields, out var err))
                return err!;
            var planId = fields["plan_id"];
            var fromNodeId = fields["from_node_id"];
            var toNodeId = fields["to_node_id"];
            var edgeType = body.TryGetProperty("edge_type", out var et)
                ? Enum.Parse<EdgeType>(et.GetString() ?? "ControlFlow", true)
                : EdgeType.ControlFlow;
            var contextKey = body.TryGetProperty("context_key", out var ck)
                ? ck.GetString() : null;
            var condition = body.TryGetProperty("condition", out var cond)
                ? cond.GetString() : null;

            if (!TryRequirePlanAccess(ctx, planService.GetPlan(planId), broker, out var denied))
                return denied;

            try
            {
                var edge = planService.AddEdge(planId, fromNodeId, toNodeId,
                    edgeType, contextKey, condition);
                return Results.Ok(ApiResponseHelper.Success(edge));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── DAG 驗證 ──
        plans.MapPost("/validate", (HttpContext ctx, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "plan_id", out var planId, out var err))
                return err!;

            if (!TryRequirePlanAccess(ctx, planService.GetPlan(planId), broker, out var denied))
                return denied;

            var (isValid, error) = planService.ValidateDag(planId);

            if (!isValid)
                return Results.BadRequest(ApiResponseHelper.Error($"DAG invalid: {error}"));

            var nodes = planService.GetNodes(planId);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                isValid,
                topologicalOrder = nodes
                    .OrderBy(n => n.Ordinal)
                    .Select(n => new { n.NodeId, n.Ordinal, n.CapabilityId })
                    .ToList()
            }));
        });

        // ── 提交並執行計畫 ──
        plans.MapPost("/submit", async (HttpContext ctx, IPlanEngine planEngine, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            var sessionId = RequestBodyHelper.GetSessionId(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "plan_id", out var planId, out var valErr))
                return valErr!;
            var traceId = body.TryGetProperty("trace_id", out var tid)
                ? tid.GetString() ?? IdGen.New("trace") : IdGen.New("trace");

            if (!TryRequirePlanAccess(ctx, planService.GetPlan(planId), broker, out var denied))
                return denied;

            // session_id 從 token claims 取得（而非 body），確保一致性
            if (string.IsNullOrEmpty(sessionId))
            {
                return Results.BadRequest(ApiResponseHelper.Error(
                    "Session ID is required. Ensure scoped_token contains a valid session."));
            }

            try
            {
                // H-3 修復：proper await，消除 sync-over-async
                // M-8 修復：傳遞 RequestAborted 取消令牌
                var plan = await planEngine.SubmitAndExecuteAsync(planId, principalId, sessionId, traceId, ctx.RequestAborted);
                return Results.Ok(ApiResponseHelper.Success(plan));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── 查詢執行狀態 ──
        plans.MapPost("/status", (HttpContext ctx, IPlanService planService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "plan_id", out var planId, out var err))
                return err!;

            var plan = planService.GetPlan(planId);
            if (plan == null)
                return Results.NotFound(ApiResponseHelper.Error("Plan not found.", 404));

            if (!TryRequirePlanAccess(ctx, plan, broker, out var denied))
                return denied;

            var nodes = planService.GetNodes(planId);
            var edges = planService.GetEdges(planId);
            var checkpoints = planService.GetCheckpoints(planId);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                plan = ForCaller(ctx, plan),
                nodes = nodes.OrderBy(n => n.Ordinal).ToList(),
                edges,
                checkpoints,
                summary = new
                {
                    totalNodes = nodes.Count,
                    succeeded = nodes.Count(n => n.State == NodeState.Succeeded),
                    failed = nodes.Count(n => n.State == NodeState.Failed),
                    pending = nodes.Count(n => n.State == NodeState.Pending),
                    running = nodes.Count(n => n.State == NodeState.Running),
                    cancelled = nodes.Count(n => n.State == NodeState.Cancelled),
                    skipped = nodes.Count(n => n.State == NodeState.Skipped)
                }
            }));
        });
    }

    /// <summary>
    /// 計畫屬於某個任務：只允許該任務的擁有者（或管理員）操作。
    /// 計畫不存在時不在此判斷，維持各端點原本的「找不到」處理。
    /// </summary>
    /// <summary>
    /// 以 token 綁定的任務取得存取權、但不是提交者的呼叫者（例如被指派到這個任務的代理）看不到提交者的識別資料；
    /// 管理員與提交者看到完整的計畫。
    /// </summary>
    private static Plan ForCaller(HttpContext ctx, Plan plan)
    {
        if (BrokerAuthorization.IsAdmin(ctx) ||
            string.Equals(plan.SubmittedBy, RequestBodyHelper.GetPrincipalId(ctx), StringComparison.Ordinal))
        {
            return plan;
        }

        return new Plan
        {
            PlanId = plan.PlanId,
            TaskId = plan.TaskId,
            SubmittedBy = string.Empty,
            Title = plan.Title,
            Description = plan.Description,
            StateValue = plan.StateValue,
            TotalNodes = plan.TotalNodes,
            CompletedNodes = plan.CompletedNodes,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt
        };
    }

    private static bool TryRequirePlanAccess(HttpContext ctx, Plan? plan, IBrokerService broker, out IResult denied)
    {
        if (plan == null)
        {
            denied = null!;
            return true;
        }

        return BrokerAuthorization.TryRequireTaskAccess(ctx, broker, plan.TaskId, out denied);
    }
}
