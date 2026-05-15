using Broker.Helpers;
using Broker.Models;
using Broker.Services;
using BrokerCore;
using BrokerCore.Data;

namespace Broker.Endpoints;

/// <summary>
/// H2 — Time ACL CRUD（admin only）
///
/// GET    /api/v1/time-acl                 — 列規則 + 當下命中狀態
/// POST   /api/v1/time-acl                 — 新增
/// PATCH  /api/v1/time-acl/{id}/enable     — 啟停
/// DELETE /api/v1/time-acl/{id}            — 刪除
/// GET    /api/v1/time-acl/test?capability_id=xxx — 看當下這個 cap 是不是在 auto window
/// </summary>
public static class TimeAclEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var t = group.MapGroup("/time-acl");

        t.MapGet("", (HttpContext ctx, BrokerDb db, TimeAclService svc) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var rules = svc.ListRules();
            var now = DateTime.UtcNow;
            return Results.Ok(ApiResponseHelper.Success(new {
                now_utc = now,
                rules = rules.Select(r => new {
                    r.RuleId,
                    capability_id = r.CapabilityId,
                    start_hour = r.StartHour,
                    end_hour = r.EndHour,
                    weekday_mask = r.WeekdayMask,
                    weekday_label = WeekdayLabel(r.WeekdayMask),
                    timezone = r.Timezone,
                    enabled = r.Enabled,
                    description = r.Description,
                    created_by = r.CreatedBy,
                    created_at = r.CreatedAt,
                    in_window_now = TimeAclService.Matches(r, now),
                }),
            }));
        });

        t.MapPost("", (HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var body = RequestBodyHelper.GetBody(ctx);
            var cap = body.TryGetProperty("capability_id", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(cap))
                return Results.BadRequest(ApiResponseHelper.Error("capability_id required"));

            var startH = body.TryGetProperty("start_hour", out var s) && s.TryGetInt32(out var sv) ? sv : 9;
            var endH = body.TryGetProperty("end_hour", out var e) && e.TryGetInt32(out var ev) ? ev : 17;
            var mask = body.TryGetProperty("weekday_mask", out var w) && w.TryGetInt32(out var wv) ? wv : 0b0111110;
            var tz = body.TryGetProperty("timezone", out var tzz) ? tzz.GetString() ?? "UTC" : "UTC";
            var desc = body.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";

            startH = Math.Clamp(startH, 0, 23);
            endH = Math.Clamp(endH, 0, 23);

            var rule = new TimeAclRule {
                RuleId = IdGen.New("tacl"),
                CapabilityId = cap,
                StartHour = startH,
                EndHour = endH,
                WeekdayMask = mask & 0b1111111,
                Timezone = tz,
                Enabled = true,
                Description = desc,
                CreatedBy = RequestBodyHelper.GetPrincipalId(ctx),
                CreatedAt = DateTime.UtcNow,
            };
            db.Insert(rule);
            return Results.Ok(ApiResponseHelper.Success(rule));
        });

        t.MapPatch("/{id}/enable", (string id, HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var body = RequestBodyHelper.GetBody(ctx);
            var enable = body.TryGetProperty("enable", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.True;
            var existing = db.Get<TimeAclRule>(id);
            if (existing == null) return Results.NotFound(ApiResponseHelper.Error($"rule {id} not found"));
            existing.Enabled = enable;
            db.Update(existing);
            return Results.Ok(ApiResponseHelper.Success(new { rule_id = id, enabled = enable }));
        });

        t.MapDelete("/{id}", (string id, HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var existing = db.Get<TimeAclRule>(id);
            if (existing == null) return Results.NotFound(ApiResponseHelper.Error($"rule {id} not found"));
            db.Delete<TimeAclRule>(existing);
            return Results.Ok(ApiResponseHelper.Success(new { rule_id = id, deleted = true }));
        });

        t.MapGet("/test", (HttpContext ctx, TimeAclService svc) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var cap = ctx.Request.Query.TryGetValue("capability_id", out var v) ? v.ToString() : "";
            if (string.IsNullOrWhiteSpace(cap))
                return Results.BadRequest(ApiResponseHelper.Error("capability_id query required"));
            var inside = svc.IsInsideAutoWindow(cap, DateTime.UtcNow);
            return Results.Ok(ApiResponseHelper.Success(new {
                capability_id = cap,
                now_utc = DateTime.UtcNow,
                in_auto_window = inside,
                effective_action = inside switch {
                    true => "走原 ACL（可能 auto 可能 require_approval、看 capability 設定）",
                    false => "強制 require_approval（時段外）",
                    null => "無 time rule 規範這個 capability、走原 ACL",
                },
            }));
        });
    }

    private static string WeekdayLabel(int mask)
    {
        var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var hits = new List<string>();
        for (int i = 0; i < 7; i++)
            if ((mask & (1 << i)) != 0) hits.Add(names[i]);
        return hits.Count == 7 ? "Everyday" : string.Join(",", hits);
    }

    private static bool RequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (RequestBodyHelper.IsAdmin(ctx)) { denied = null!; return true; }
        denied = Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);
        return false;
    }
}
