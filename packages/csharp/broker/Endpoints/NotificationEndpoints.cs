using Broker.Helpers;
using Broker.Services;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Discord 通知的管理 / 測試 API。
///
///   GET  /api/v1/notifications/status
///        → { enabled, masked_webhook, interval_seconds }
///
///   POST /api/v1/notifications/test
///        → body (optional): { "message": "自訂測試訊息" }
///        → 送一則測試 embed 到設定好的 webhook
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
            string? message = null;
            try
            {
                using var reader = new StreamReader(req.Body);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var doc = JsonDocument.Parse(body).RootElement;
                    if (doc.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                        message = m.GetString();
                }
            }
            catch { /* 忽略 body parse 錯誤，用 default 訊息 */ }

            var (ok, error) = await svc.SendTestAsync(message);
            return ok
                ? Results.Ok(ApiResponseHelper.Success(new { sent = true }))
                : Results.Ok(ApiResponseHelper.Error(error ?? "unknown error"));
        });
    }
}
