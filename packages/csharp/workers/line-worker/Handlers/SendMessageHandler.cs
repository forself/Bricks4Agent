using System.Text.Json;
using WorkerSdk;

namespace LineWorker.Handlers;

/// <summary>
/// line.message.send 能力處理器 — 發送文字訊息至 LINE
///
/// payload:
///   to       (string, optional) — 接收者 LINE userId，省略則用預設接收者
///   text     (string, required) — 訊息內容
///   format   (string, optional) — "plain" (預設) 或 "flex"
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
