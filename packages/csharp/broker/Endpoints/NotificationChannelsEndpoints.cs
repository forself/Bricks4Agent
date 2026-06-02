using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// 每個用戶的推播頻道管理 API。要登入;admin 看全部、user 看自己。
///
///   GET    /api/v1/notification-channels                列我的（admin: 全部）
///   POST   /api/v1/notification-channels                新增 { channel_type: discord|line, target, label? }
///   POST   /api/v1/notification-channels/{id}/disable    暫停
///   POST   /api/v1/notification-channels/{id}/enable     恢復
///   DELETE /api/v1/notification-channels/{id}            刪
///   POST   /api/v1/notification-channels/{id}/test       送測試訊息（MVP 支援 discord）
///
/// 多用戶:朋友登記自己的 Discord webhook → 每日彙整推到他自己頻道(DailyReportService 迭代)。
/// target 是 secret、service 端加密存、回應只回遮罩。
/// </summary>
public static class NotificationChannelsEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var nc = group.MapGroup("/notification-channels");

        nc.MapGet("/", (NotificationChannelService svc, HttpContext ctx) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
            var rows = svc.ListForViewer(pid, isAdmin);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = rows.Count,
                viewer = new { principal_id = pid, role, scope = isAdmin ? "all" : "self" },
                channels = rows.Select(r => new
                {
                    entry_id = r.EntryId,
                    owner_principal_id = r.OwnerPrincipalId,
                    channel_type = r.ChannelType,
                    label = r.Label,
                    disabled = r.Disabled,
                    target_masked = r.TargetMasked,
                    created_at = r.CreatedAt,
                    updated_at = r.UpdatedAt,
                    last_used_at = r.LastUsedAt,
                }),
            }));
        });

        nc.MapPost("/", async (NotificationChannelService svc, HttpContext ctx) =>
        {
            var (pid, _) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string channelType = "", target = "", label = "";
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                channelType = doc.TryGetProperty("channel_type", out var t) ? t.GetString() ?? "" : "";
                target = doc.TryGetProperty("target", out var tg) ? tg.GetString() ?? "" : "";
                label = doc.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            try
            {
                var view = svc.Create(pid, channelType, target, label);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    entry_id = view.EntryId,
                    channel_type = view.ChannelType,
                    label = view.Label,
                }));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        nc.MapDelete("/{entryId}", (NotificationChannelService svc, HttpContext ctx, string entryId) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var ok = svc.Delete(entryId, pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(ApiResponseHelper.Success(new { removed = ok }));
            }
            catch (UnauthorizedAccessException) { return Results.Json(ApiResponseHelper.Error("Not your channel", 403), statusCode: 403); }
        });

        nc.MapPost("/{entryId}/disable", (NotificationChannelService svc, HttpContext ctx, string entryId) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var ok = svc.SetDisabled(entryId, true, pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(ApiResponseHelper.Success(new { disabled = true, ok }));
            }
            catch (UnauthorizedAccessException) { return Results.Json(ApiResponseHelper.Error("Not your channel", 403), statusCode: 403); }
        });

        nc.MapPost("/{entryId}/enable", (NotificationChannelService svc, HttpContext ctx, string entryId) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var ok = svc.SetDisabled(entryId, false, pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(ApiResponseHelper.Success(new { disabled = false, ok }));
            }
            catch (UnauthorizedAccessException) { return Results.Json(ApiResponseHelper.Error("Not your channel", 403), statusCode: 403); }
        });

        nc.MapPost("/{entryId}/test", async (NotificationChannelService svc, DiscordNotificationService discord,
            HttpContext ctx, string entryId, CancellationToken ct) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);

            var existing = svc.ListForViewer(pid, isAdmin).FirstOrDefault(r => r.EntryId == entryId);
            if (existing == null) return Results.NotFound(ApiResponseHelper.Error("Channel not found"));

            var resolved = svc.Resolve(existing.OwnerPrincipalId, existing.ChannelType);
            if (resolved == null) return Results.Ok(ApiResponseHelper.Error("Decryption failed — master key may have changed"));

            if (resolved.ChannelType != "discord")
                return Results.Ok(ApiResponseHelper.Error($"Test for '{resolved.ChannelType}' not wired yet (MVP supports discord)"));

            var (ok, err) = await discord.SendAdHocToWebhookAsync(resolved.Target,
                "🧪 B4A 推播測試", $"你的推播頻道設定成功，每日彙整會推到這裡。\nprincipal: {pid}", ct: ct);
            return ok
                ? Results.Ok(ApiResponseHelper.Success(new { sent = true, channel_type = resolved.ChannelType }))
                : Results.Ok(ApiResponseHelper.Error("Send failed: " + err));
        });
    }
}
