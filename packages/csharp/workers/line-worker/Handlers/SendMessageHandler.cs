using System.Text.Json;
using WorkerSdk;

namespace LineWorker.Handlers;

/// <summary>
/// line.message.send 能力處理器 — 發送 LINE 訊息（文字或 Flex / 任意 message 物件陣列）
///
/// payload （二擇一）：
///   { to, text }                    — 文字訊息（預設）
///   { to, messages: [ ... ] }       — 任意 LINE message object 陣列（最多 5 則）
///                                      用來推 Flex Message（含 postback 按鈕）等富訊息
///   to     (string, optional)        — 接收者 LINE userId，省略則用預設接收者
/// 注意：messages 陣列直接 forward 到 LINE Push API、不做欄位重整、由呼叫端確保格式正確
/// </summary>
public class SendMessageHandler : ICapabilityHandler
{
    private readonly LineApiClient _lineApi;
    private readonly string _defaultRecipientId;

    public string CapabilityId => "line.message.send";

    public SendMessageHandler(LineApiClient lineApi, string defaultRecipientId)
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

            // messages 陣列分支：直接 forward 給 LINE Push API（給 Flex 等富訊息用）
            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                if (messagesEl.GetArrayLength() == 0)
                    return (false, null, "messages array is empty.");
                if (messagesEl.GetArrayLength() > 5)
                    return (false, null, "messages array exceeds LINE limit of 5 per push.");

                // 反序列化成 object[]、由 LineApiClient.PushMessagesAsync 拿去 JSON 序列化送出
                var rawMessages = JsonSerializer.Deserialize<object[]>(messagesEl.GetRawText())
                                  ?? Array.Empty<object>();
                var (sentOk, sendErr) = await _lineApi.PushMessagesAsync(to, rawMessages, ct);
                if (!sentOk)
                    return (false, null, sendErr);

                var richResult = JsonSerializer.Serialize(new
                {
                    sent = true,
                    to,
                    messageCount = rawMessages.Length,
                    format = "rich"
                });
                return (true, richResult, null);
            }

            var text = root.GetProperty("text").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return (false, null, "Message text is empty.");

            // 截斷超長訊息（LINE 限制 5000 字元）
            if (text.Length > 5000)
                text = text[..4990] + "\n...[truncated]";

            var (success, error) = await _lineApi.PushTextMessageAsync(to, text, ct);

            if (!success)
                return (false, null, error);

            var result = JsonSerializer.Serialize(new
            {
                sent = true,
                to,
                textLength = text.Length
            });

            return (true, result, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"SendMessage error: {ex.Message}");
        }
    }
}
