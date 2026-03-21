using System.Text.Json;
using WorkerSdk;

namespace LineWorker.Handlers;

/// <summary>
/// line.notification.send 能力處理器 — 發送結構化通知至 LINE
///
/// 與 line.message.send 的差異：通知有固定格式（標題 + 內容 + 可選動作）。
///
/// payload:
///   to       (string, optional) — 接收者，省略則用預設
///   title    (string, required) — 通知標題
///   body     (string, required) — 通知內容
///   level    (string, optional) — "info" / "warning" / "error" / "success"，預設 "info"
///   actions  (array, optional)  — 可選的回覆選項，如 ["approve", "deny"]
/// </summary>
public class SendNotificationHandler : ICapabilityHandler
{
    private readonly LineApiClient _lineApi;
    private readonly string _defaultRecipientId;

    public string CapabilityId => "line.notification.send";

    public SendNotificationHandler(LineApiClient lineApi, string defaultRecipientId)
    {
        _lineApi = lineApi;
        _defaultRecipientId = defaultRecipientId;
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            // Broker 傳來的 payload 格式為 { route, args }，實際參數在 args 內
            var root = doc.RootElement.TryGetProperty("args", out var argsEl)
                ? argsEl
                : doc.RootElement;

            var to = root.TryGetProperty("to", out var toProp)
                ? toProp.GetString() ?? _defaultRecipientId
                : _defaultRecipientId;

            if (string.IsNullOrEmpty(to))
                return (false, null, "No recipient specified and no default recipient configured.");

            var title = root.GetProperty("title").GetString() ?? "";
            var body = root.GetProperty("body").GetString() ?? "";
            var level = root.TryGetProperty("level", out var lvl)
                ? lvl.GetString() ?? "info"
                : "info";

            var icon = level switch
            {
                "success" => "\u2705",
                "warning" => "\u26a0\ufe0f",
                "error" => "\u274c",
                _ => "\u2139\ufe0f"
            };

            // 組裝通知訊息
            var message = $"{icon} {title}\n\n{body}";

            // 如果有 actions，附加提示
            if (root.TryGetProperty("actions", out var actions) &&
                actions.ValueKind == JsonValueKind.Array)
            {
                var actionList = actions.EnumerateArray()
                    .Select(a => a.GetString() ?? "")
                    .Where(a => !string.IsNullOrEmpty(a))
                    .ToList();

                if (actionList.Count > 0)
                {
                    message += $"\n\n---\nReply: {string.Join(" / ", actionList)}";
                }
            }

            if (message.Length > 5000)
                message = message[..4990] + "\n...[truncated]";

            var (success, error) = await _lineApi.PushTextMessageAsync(to, message, ct);

            if (!success)
                return (false, null, error);

            var result = JsonSerializer.Serialize(new
            {
                sent = true,
                to,
                level,
                titleLength = title.Length,
                bodyLength = body.Length
            });

            return (true, result, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"SendNotification error: {ex.Message}");
        }
    }
}
