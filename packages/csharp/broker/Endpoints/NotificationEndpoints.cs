using Broker.Helpers;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
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
///   POST /api/v1/notifications/line/send     → LINE 推任意 user（body: {"to":"U...","text":"..."}）
///                                              給 bot-node 收到 LINE webhook 後回覆用
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

        // bot-node LINE webhook 收到 user 訊息、跑 LLM、用這條 endpoint 把回覆 push 回 user。
        // body: { "to": "U...", "text": "..." }
        // 走 system principal 派發 line.message.send capability、繞過 ACL（caller 已過 bot token auth）。
        // line.message.send **不在** approval gate 受控集合、不會被 admin 攔下。
        n.MapPost("/line/send", async (HttpRequest req, IExecutionDispatcher dispatcher, IWorkerRegistry registry) =>
        {
            if (!registry.HasAvailableWorker("line.message.send"))
                return Results.Ok(ApiResponseHelper.Error("line-worker not connected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            string to = "", text = "";
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                to   = doc.TryGetProperty("to",   out var t)  ? (t.GetString()  ?? "") : "";
                text = doc.TryGetProperty("text", out var tx) ? (tx.GetString() ?? "") : "";
            }
            catch { /* 空 body / 壞 JSON 一律當錯誤 */ }

            if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(text))
                return Results.BadRequest(ApiResponseHelper.Error("`to` and `text` required"));

            var argsPayload = JsonSerializer.Serialize(new { args = new { to, text } });
            var dispatchReq = new ApprovedRequest
            {
                RequestId    = Guid.NewGuid().ToString("N"),
                CapabilityId = "line.message.send",
                Route        = "send",
                Payload      = argsPayload,
                Scope        = "{}",
                PrincipalId  = "system",  // 內部使用、繞 ACL（非 trading 類、無 approval gate）
                TaskId       = "bot-line-reply",
                SessionId    = "bot-line-reply",
            };
            var result = await dispatcher.DispatchAsync(dispatchReq);
            return result.Success
                ? Results.Ok(ApiResponseHelper.Success(new { sent = true, to_prefix = to[..Math.Min(8, to.Length)] + "…" }))
                : Results.Ok(ApiResponseHelper.Error(result.ErrorMessage ?? "dispatch failed"));
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
