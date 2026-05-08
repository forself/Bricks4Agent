using Broker.Helpers;
using Broker.Services;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// 通知（Discord + LINE）管理 / 測試 API。兩個 channel 各自獨立 enable／disable。
///
///   GET  /api/v1/notifications/status        → Discord 狀態（向後相容路徑）
///   POST /api/v1/notifications/test          → Discord 測試送一則
///
///   GET  /api/v1/notifications/line/status   → LINE 狀態（enabled, worker_available, recipient）
///   POST /api/v1/notifications/line/test     → LINE 測試送一則（body: {"message":"..."}）
/// </summary>
public static class NotificationEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var n = group.MapGroup("/notifications");

        n.MapGet("/status", (DiscordNotificationService svc) =>
        {
            return Results.Ok(ApiResponseHelper.Success(new
            {
                enabled = svc.IsEnabled,
                masked_webhook = svc.MaskedWebhook,
                interval_seconds = svc.IntervalSeconds,
            }));
        });

        n.MapPost("/test", async (DiscordNotificationService svc, HttpRequest req) =>
        {
            string? message = await ReadOptionalMessage(req);
            var (ok, error) = await svc.SendTestAsync(message);
            return ok
                ? Results.Ok(ApiResponseHelper.Success(new { sent = true }))
                : Results.Ok(ApiResponseHelper.Error(error ?? "unknown error"));
        });

        n.MapGet("/line/status", (LineNotificationService svc) =>
        {
            return Results.Ok(ApiResponseHelper.Success(new
            {
                enabled_in_config = svc.IsEnabledInConfig,
                worker_available = svc.IsWorkerAvailable,
                active = svc.IsActive,
                masked_recipient = svc.MaskedRecipient,
                interval_seconds = svc.IntervalSeconds,
            }));
        });

        n.MapPost("/line/test", async (LineNotificationService svc, HttpRequest req) =>
        {
            string? message = await ReadOptionalMessage(req);
            var (ok, error) = await svc.SendTestAsync(message);
            return ok
                ? Results.Ok(ApiResponseHelper.Success(new { sent = true }))
                : Results.Ok(ApiResponseHelper.Error(error ?? "unknown error"));
        });
    }

    private static async Task<string?> ReadOptionalMessage(HttpRequest req)
    {
        try
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body)) return null;
            var doc = JsonDocument.Parse(body).RootElement;
            if (doc.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString();
        }
        catch { /* 忽略 body parse 錯誤，用 default 訊息 */ }
        return null;
    }
}
