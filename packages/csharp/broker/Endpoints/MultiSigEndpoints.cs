using Broker.Helpers;
using Broker.Models;
using BrokerCore.Data;

namespace Broker.Endpoints;

/// <summary>
/// I1 — Multi-sig rule + approval decisions（admin only）
///
/// GET    /api/v1/multi-sig                — 列規則
/// PUT    /api/v1/multi-sig                — upsert（capability_id, min_approvers, enabled, description）
/// DELETE /api/v1/multi-sig/{capability_id} — 移除
/// GET    /api/v1/multi-sig/decisions/{approval_id} — 看單一 approval 的所有簽核決定
/// </summary>
public static class MultiSigEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var ms = group.MapGroup("/multi-sig");

        ms.MapGet("", (HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var rules = db.Query<MultiSigRule>(
                "SELECT * FROM multi_sig_rules ORDER BY created_at DESC LIMIT 200");
            return Results.Ok(ApiResponseHelper.Success(rules));
        });

        ms.MapPut("", (HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var body = RequestBodyHelper.GetBody(ctx);
            var cap = body.TryGetProperty("capability_id", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(cap))
                return Results.BadRequest(ApiResponseHelper.Error("capability_id required"));
            var min = body.TryGetProperty("min_approvers", out var m) && m.TryGetInt32(out var mv)
                ? Math.Clamp(mv, 1, 10) : 2;
            var enabled = !body.TryGetProperty("enabled", out var en) || en.ValueKind != System.Text.Json.JsonValueKind.False;
            var desc = body.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";

            var existing = db.Get<MultiSigRule>(cap);
            if (existing == null)
            {
                db.Insert(new MultiSigRule {
                    CapabilityId = cap, MinApprovers = min, Enabled = enabled,
                    Description = desc, CreatedBy = RequestBodyHelper.GetPrincipalId(ctx),
                    CreatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.MinApprovers = min;
                existing.Enabled = enabled;
                existing.Description = desc;
                db.Update(existing);
            }
            return Results.Ok(ApiResponseHelper.Success(new { capability_id = cap, min_approvers = min, enabled }));
        });

        ms.MapDelete("/{capabilityId}", (string capabilityId, HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var existing = db.Get<MultiSigRule>(capabilityId);
            if (existing == null) return Results.NotFound(ApiResponseHelper.Error($"rule for {capabilityId} not found"));
            db.Delete<MultiSigRule>(existing);
            return Results.Ok(ApiResponseHelper.Success(new { capability_id = capabilityId, deleted = true }));
        });

        ms.MapGet("/decisions/{approvalId}", (string approvalId, HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var decisions = db.Query<ApprovalDecisionRecord>(
                "SELECT * FROM approval_decisions WHERE approval_id = @aid ORDER BY decided_at ASC",
                new { aid = approvalId });
            var approved = decisions.Count(x => x.Decision == "approved");
            var rejected = decisions.Count(x => x.Decision == "rejected");
            return Results.Ok(ApiResponseHelper.Success(new {
                approval_id = approvalId,
                total_decisions = decisions.Count,
                approved_count = approved,
                rejected_count = rejected,
                decisions,
            }));
        });
    }

    private static bool RequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (RequestBodyHelper.IsAdmin(ctx)) { denied = null!; return true; }
        denied = Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);
        return false;
    }
}
