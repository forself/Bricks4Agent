using System.Text.Json;
using Broker.Helpers;
using Broker.Models;
using Broker.Services;
using BrokerCore;
using BrokerCore.Data;

namespace Broker.Endpoints;

/// <summary>
/// H3 — Approval template CRUD（admin only）
///
/// GET    /api/v1/approval-templates                — 列全部
/// POST   /api/v1/approval-templates                — 新增
/// PATCH  /api/v1/approval-templates/{id}/enable    — 啟用 / 停用（body {enable:bool}）
/// DELETE /api/v1/approval-templates/{id}           — 刪除
/// GET    /api/v1/approval-templates/hits           — 看當日命中次數 snapshot
/// </summary>
public static class ApprovalTemplateEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var ap = group.MapGroup("/approval-templates");

        ap.MapGet("", (HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var list = db.Query<ApprovalTemplate>(
                "SELECT * FROM approval_templates ORDER BY created_at DESC LIMIT 200");
            return Results.Ok(ApiResponseHelper.Success(list));
        });

        ap.MapPost("", (HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            var cap = body.TryGetProperty("capability_id", out var c) ? c.GetString() ?? "" : "";
            var route = body.TryGetProperty("route", out var r) ? r.GetString() ?? "" : "";
            var match = body.TryGetProperty("payload_match", out var m) ? m.GetRawText() : "{}";
            var maxUses = body.TryGetProperty("max_uses_per_day", out var mu) && mu.TryGetInt32(out var mui) ? mui : 0;
            var desc = body.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(cap))
                return Results.BadRequest(ApiResponseHelper.Error("capability_id required"));

            // payload_match 必須是合法 JSON object
            try
            {
                using var doc = JsonDocument.Parse(match);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Results.BadRequest(ApiResponseHelper.Error("payload_match must be a JSON object"));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error($"payload_match invalid JSON: {ex.Message}"));
            }

            var t = new ApprovalTemplate {
                TemplateId = IdGen.New("aptpl"),
                CapabilityId = cap,
                Route = route,
                PayloadMatch = match,
                MaxUsesPerDay = Math.Max(0, maxUses),
                Enabled = true,
                Description = desc,
                CreatedBy = RequestBodyHelper.GetPrincipalId(ctx),
                CreatedAt = DateTime.UtcNow,
            };
            db.Insert(t);
            return Results.Ok(ApiResponseHelper.Success(t));
        });

        ap.MapPatch("/{id}/enable", (string id, HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            var enable = body.TryGetProperty("enable", out var e) && e.ValueKind == JsonValueKind.True;
            var existing = db.Get<ApprovalTemplate>(id);
            if (existing == null) return Results.NotFound(ApiResponseHelper.Error($"template {id} not found"));
            existing.Enabled = enable;
            db.Update(existing);
            return Results.Ok(ApiResponseHelper.Success(new { template_id = id, enabled = enable }));
        });

        ap.MapDelete("/{id}", (string id, HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var existing = db.Get<ApprovalTemplate>(id);
            if (existing == null) return Results.NotFound(ApiResponseHelper.Error($"template {id} not found"));
            db.Delete<ApprovalTemplate>(existing);
            return Results.Ok(ApiResponseHelper.Success(new { template_id = id, deleted = true }));
        });

        ap.MapGet("/hits", (HttpContext ctx, ApprovalTemplateMatcher matcher) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var snap = matcher.Snapshot();
            return Results.Ok(ApiResponseHelper.Success(new {
                day_utc = DateTime.UtcNow.Date,
                hits = snap.Select(kv => new { template_id = kv.Key, count = kv.Value })
                    .OrderByDescending(x => x.count),
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
