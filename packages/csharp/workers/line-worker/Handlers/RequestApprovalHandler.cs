using System.Text.Json;
using WorkerSdk;

namespace LineWorker.Handlers;

/// <summary>
/// line.approval.request 能力處理器 — 發送審批請求至 LINE 並等待回覆
///
/// 流程：
/// 1. 發送審批通知到 LINE（含操作描述）
/// 2. 在 InboundDispatcher 註冊等待
/// 3. 輪詢等待回覆（approve / deny）
/// 4. 回傳審批結果
///
/// payload:
///   to            (string, optional) — 審批者，省略則用預設
///   description   (string, required) — 審批內容描述
///   request_id    (string, required) — 關聯的請求 ID
///   timeout_sec   (int, optional)    — 等待超時秒數，預設 300
/// </summary>
public class RequestApprovalHandler : ICapabilityHandler
{
    private readonly LineApiClient _lineApi;
    private readonly InboundDispatcher _dispatcher;
    private readonly string _defaultRecipientId;

    public string CapabilityId => "line.approval.request";

    public RequestApprovalHandler(LineApiClient lineApi, InboundDispatcher dispatcher, string defaultRecipientId)
    {
        _lineApi = lineApi;
        _dispatcher = dispatcher;
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
                return (false, null, "No recipient specified.");

            var description = root.GetProperty("description").GetString() ?? "";
            var relatedRequestId = root.TryGetProperty("request_id", out var rid)
                ? rid.GetString() ?? requestId
                : requestId;

            var timeoutSec = root.TryGetProperty("timeout_sec", out var ts)
                ? ts.GetInt32()
                : 300;

            // 用 requestId 作為 approvalKey
            var approvalKey = requestId;

            // 發送審批通知
            var message =
                $"\u2753 Approval Required\n\n" +
                $"{description}\n\n" +
                $"Request: {relatedRequestId}\n" +
                $"---\n" +
                $"Reply: approve / deny";

            var (sent, sendError) = await _lineApi.PushTextMessageAsync(to, message, ct);
            if (!sent)
                return (false, null, $"Failed to send approval request: {sendError}");

            // 註冊等待
            _dispatcher.RegisterApproval(approvalKey, relatedRequestId, description);

            // 輪詢等待回覆
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var approvalResult = _dispatcher.GetApprovalResult(approvalKey);
                if (approvalResult != null)
                {
                    var result = JsonSerializer.Serialize(new
                    {
                        approved = approvalResult.Approved,
                        respondedBy = approvalResult.RespondedBy,
                        respondedAt = approvalResult.RespondedAt.ToString("O"),
                        requestId = relatedRequestId
                    });

                    return (true, result, null);
                }

                await Task.Delay(1000, ct);
            }

            // 超時
            var timeoutResult = JsonSerializer.Serialize(new
            {
                approved = false,
                timedOut = true,
                requestId = relatedRequestId
            });

            return (true, timeoutResult, null);
        }
        catch (OperationCanceledException)
        {
            return (false, null, "Approval request cancelled.");
        }
        catch (Exception ex)
        {
            return (false, null, $"RequestApproval error: {ex.Message}");
        }
    }
}
