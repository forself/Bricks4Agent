using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// G2 — Live audit event WebSocket
///
/// ws(s)://host/ws/audit/stream
///
/// 連線後 broker 推一個 hello frame、之後每筆 AuditService.RecordEvent 廣播一份。
/// JSON 格式：
///   {
///     "type": "event",
///     "event_id": 12345, "trace_id": "trc_...", "trace_seq": 0,
///     "event_type": "KILL_SWITCH", "principal_id": "...", "occurred_at": "...",
///     "previous_event_hash": "abc123...", "event_hash": "def456...",
///     "details": "{...}"
///   }
///
/// 客戶端可送 {"filter": "KILL_SWITCH"} 過濾、broker 不過濾、純 client-side render。
/// 沒有 backpressure / replay；missed events 從 /api/v1/audit 補拉（已存 DB）。
///
/// 認證：cookie session role=admin（CurrentUserMiddleware 在 WS handshake 前已跑、
/// HttpContext.Items 有 role）。non-admin 直接 403。
/// </summary>
public static class AuditStreamEndpoints
{
    public static void Map(WebApplication app)
    {
        app.Map("/ws/audit/stream", async (HttpContext ctx, AuditEventBus bus, ILoggerFactory lf) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("WebSocket required");
                return;
            }

            // 只給 admin（dashboard cookie session）
            var role = ctx.Items[CurrentUserMiddleware.RoleKey] as string;
            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Admin role required for audit stream");
                return;
            }

            var logger = lf.CreateLogger("AuditStream");
            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var (reader, unsubscribe) = bus.Subscribe();
            logger.LogInformation("Audit stream subscriber connected (total={N})", bus.SubscriberCount);

            // hello frame
            await SendJsonAsync(ws, new
            {
                type = "hello",
                server_time = DateTime.UtcNow,
                subscribers = bus.SubscriberCount,
                message = "Subscribed to AuditService.RecordEvent broadcast",
            }, ctx.RequestAborted);

            try
            {
                // 客戶端可隨時 close
                _ = Task.Run(async () =>
                {
                    var buf = new byte[1024];
                    try
                    {
                        while (ws.State == WebSocketState.Open)
                        {
                            var r = await ws.ReceiveAsync(buf, ctx.RequestAborted);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                        }
                    }
                    catch { }
                });

                // 主推送 loop
                await foreach (var ev in reader.ReadAllAsync(ctx.RequestAborted))
                {
                    if (ws.State != WebSocketState.Open) break;
                    await SendJsonAsync(ws, new
                    {
                        type = "event",
                        event_id = ev.EventId,
                        trace_id = ev.TraceId,
                        trace_seq = ev.TraceSeq,
                        event_type = ev.EventType,
                        principal_id = ev.PrincipalId,
                        task_id = ev.TaskId,
                        session_id = ev.SessionId,
                        resource_ref = ev.ResourceRef,
                        previous_event_hash = ev.PreviousEventHash,
                        event_hash = ev.EventHash,
                        occurred_at = ev.OccurredAt,
                        details = ev.Details,
                    }, ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Audit stream subscriber error");
            }
            finally
            {
                unsubscribe();
                if (ws.State == WebSocketState.Open)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                    catch { }
                }
                logger.LogInformation("Audit stream subscriber disconnected (total={N})", bus.SubscriberCount);
            }
        });
    }

    private static async Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}
