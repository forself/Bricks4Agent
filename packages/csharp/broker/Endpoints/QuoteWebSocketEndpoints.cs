using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Endpoints;

/// <summary>
/// WebSocket 端點 — 即時推播報價更新。
///
/// ws://localhost:5000/ws/quotes
/// 客戶端連線後每 N 秒收到一次報價 JSON。
/// </summary>
public static class QuoteWebSocketEndpoints
{
    public static void Map(WebApplication app)
    {
        app.Map("/ws/quotes", async (
            HttpContext context,
            IWorkerRegistry registry,
            IExecutionDispatcher dispatcher) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            var intervalMs = 5000; // 每 5 秒推一次

            // 讀取客戶端設定（選填）
            var buffer = new byte[256];
            _ = Task.Run(async () =>
            {
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        // 客戶端可以送 {"interval": 3000} 改變推播間隔
                        try
                        {
                            var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            var doc = JsonDocument.Parse(msg);
                            if (doc.RootElement.TryGetProperty("interval", out var iv))
                                intervalMs = Math.Max(1000, iv.GetInt32());
                        }
                        catch { }
                    }
                }
                catch { }
            });

            // 推播迴圈
            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    if (registry.HasAvailableWorker("quote.prices"))
                    {
                        var request = new ApprovedRequest
                        {
                            RequestId = Guid.NewGuid().ToString("N"),
                            CapabilityId = "quote.prices",
                            Route = "get_prices",
                            Payload = "{}",
                            Scope = "{}",
                            PrincipalId = "ws",
                            TaskId = "ws-quotes",
                            SessionId = "ws-quotes"
                        };

                        var result = await dispatcher.DispatchAsync(request);
                        if (result.Success && result.ResultPayload != null)
                        {
                            var bytes = Encoding.UTF8.GetBytes(result.ResultPayload);
                            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }
                catch (WebSocketException) { break; }
                catch { }

                await Task.Delay(intervalMs);
            }

            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        });
    }
}
