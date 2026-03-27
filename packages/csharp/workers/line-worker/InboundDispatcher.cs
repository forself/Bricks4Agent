using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LineWorker;

/// <summary>
/// Dispatches inbound LINE messages.
///
/// Responsibilities:
/// - handle approval replies
/// - forward normal messages to the broker high-level coordinator
/// - route slash commands
/// - accept future audio/STT expansion
/// </summary>
public class InboundDispatcher
{
    private readonly WebhookReceiver _receiver;
    private readonly LineApiClient _lineApi;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _brokerApiUrl;
    private readonly HashSet<string> _allowedUserIds;
    private readonly Dictionary<string, PendingApproval> _pendingApprovals = new();
    private readonly object _approvalLock = new();
    private readonly TimeSpan _notificationPollInterval;
    private DateTime _lastNotificationPollAt = DateTime.MinValue;

    public InboundDispatcher(
        WebhookReceiver receiver,
        LineApiClient lineApi,
        string allowedUserIdsCsv,
        string brokerApiUrl,
        ILogger logger,
        TimeSpan? notificationPollInterval = null)
    {
        _receiver = receiver;
        _lineApi = lineApi;
        _logger = logger;
        _brokerApiUrl = brokerApiUrl.TrimEnd('/');
        _notificationPollInterval = notificationPollInterval ?? TimeSpan.FromSeconds(5);
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        _allowedUserIds = new HashSet<string>(
            (allowedUserIdsCsv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("InboundDispatcher started (broker={Broker})", _brokerApiUrl);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_receiver.InboundQueue.TryDequeue(out var message))
                {
                    await ProcessMessage(message, ct);
                }
                else
                {
                    await Task.Delay(200, ct);
                }

                if (DateTime.UtcNow - _lastNotificationPollAt >= _notificationPollInterval)
                {
                    _lastNotificationPollAt = DateTime.UtcNow;
                    await DispatchPendingNotifications(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void RegisterApproval(string approvalKey, string requestId, string description)
    {
        lock (_approvalLock)
        {
            _pendingApprovals[approvalKey] = new PendingApproval
            {
                ApprovalKey = approvalKey,
                RequestId = requestId,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };
        }
    }

    public ApprovalResult? GetApprovalResult(string approvalKey)
    {
        lock (_approvalLock)
        {
            if (_pendingApprovals.TryGetValue(approvalKey, out var pending) && pending.Result != null)
            {
                _pendingApprovals.Remove(approvalKey);
                return pending.Result;
            }
        }

        return null;
    }

    private async Task ProcessMessage(InboundMessage message, CancellationToken ct)
    {
        if (_allowedUserIds.Count > 0 && !_allowedUserIds.Contains(message.UserId))
        {
            _logger.LogWarning("Ignoring message from non-whitelisted user {UserId}", message.UserId);
            return;
        }

        var text = message.Type switch
        {
            InboundMessageType.Text => message.Text ?? "",
            InboundMessageType.Audio => await ProcessAudio(message, ct),
            _ => message.Text ?? ""
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim().ToLowerInvariant();
        if (TryHandleApproval(trimmed, message))
        {
            return;
        }

        if (text.Trim().StartsWith("/"))
        {
            await HandleCommand(text.Trim(), message, ct);
            return;
        }

        await ForwardToBrokerAndReply(message.UserId, text, ct);
    }

    private async Task ForwardToBrokerAndReply(string userId, string text, CancellationToken ct)
    {
        _logger.LogInformation(
            "Forwarding message from {User} to Broker: {Text}",
            userId[..Math.Min(8, userId.Length)],
            text.Length > 50 ? text[..50] + "..." : text);

        try
        {
            var requestBody = JsonSerializer.Serialize(new { user_id = userId, message = text });
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync($"{_brokerApiUrl}/api/v1/high-level/line/process", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Broker returned {Status}: {Body}", response.StatusCode, responseBody);
                await _lineApi.PushTextMessageAsync(userId, "目前無法將訊息交給中介核心處理，請稍後再試。", ct);
                return;
            }

            using var doc = JsonDocument.Parse(responseBody);
            string? reply = null;
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("reply", out var nestedReply))
            {
                reply = nestedReply.GetString();
            }
            else if (doc.RootElement.TryGetProperty("reply", out var directReply))
            {
                reply = directReply.GetString();
            }

            if (string.IsNullOrWhiteSpace(reply))
            {
                reply = "中介核心已收到請求，但沒有可回傳的訊息。";
            }

            if (reply.Length > 4900)
            {
                reply = reply[..4900] + "\n\n... (訊息已截斷)";
            }

            var (success, error) = await _lineApi.PushTextMessageAsync(userId, reply, ct);
            if (success)
            {
                _logger.LogInformation(
                    "Reply sent to {User}: {Reply}",
                    userId[..Math.Min(8, userId.Length)],
                    reply.Length > 80 ? reply[..80] + "..." : reply);
            }
            else
            {
                _logger.LogError("Failed to send reply to {User}: {Error}", userId, error);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Request to Broker timed out for user {User}", userId);
            await _lineApi.PushTextMessageAsync(userId, "目前處理逾時，請稍後再試。", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding message for user {User}", userId);
            await _lineApi.PushTextMessageAsync(userId, "處理訊息時發生錯誤，請稍後再試。", ct);
        }
    }

    private async Task HandleCommand(string command, InboundMessage message, CancellationToken ct)
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/help":
                await _lineApi.PushTextMessageAsync(
                    message.UserId,
                    "可用指令：\n/help - 顯示說明\n/clear - 清除對話記錄\n/status - 查看系統狀態",
                    ct);
                break;

            case "/clear":
                try
                {
                    await _httpClient.DeleteAsync($"{_brokerApiUrl}/dev/conversations/{message.UserId}", ct);
                    await _lineApi.PushTextMessageAsync(message.UserId, "對話記錄已清除。", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to clear conversation");
                    await _lineApi.PushTextMessageAsync(message.UserId, "清除對話記錄失敗，請稍後再試。", ct);
                }
                break;

            case "/status":
                try
                {
                    var resp = await _httpClient.GetStringAsync($"{_brokerApiUrl}/dev/system/status", ct);
                    using var doc = JsonDocument.Parse(resp);
                    var status = doc.RootElement.GetProperty("status").GetString();
                    var llmOk = doc.RootElement.GetProperty("services").GetProperty("llm").GetProperty("ok").GetBoolean();
                    var model = doc.RootElement.GetProperty("services").GetProperty("llm").GetProperty("model").GetString();
                    var convs = doc.RootElement.GetProperty("database").GetProperty("active_conversations").GetInt32();

                    await _lineApi.PushTextMessageAsync(
                        message.UserId,
                        $"系統狀態：{(status == "ok" ? "正常" : "異常")}\n" +
                        $"LLM：{(llmOk ? "可用" : "不可用")} ({model})\n" +
                        $"對話數：{convs}",
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get system status");
                    await _lineApi.PushTextMessageAsync(message.UserId, "目前無法取得系統狀態。", ct);
                }
                break;

            default:
                await _lineApi.PushTextMessageAsync(
                    message.UserId,
                    $"未知指令：{cmd}\n請輸入 /help 查看可用指令。",
                    ct);
                break;
        }
    }

    private bool TryHandleApproval(string text, InboundMessage message)
    {
        bool? isApproved = null;
        string? targetKey = null;

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];

        if (command is "approve" or "approved" or "y" or "yes")
        {
            isApproved = true;
        }
        else if (command is "deny" or "denied" or "n" or "no")
        {
            isApproved = false;
        }
        else
        {
            return false;
        }

        if (parts.Length > 1)
        {
            targetKey = parts[1].Trim();
        }

        lock (_approvalLock)
        {
            PendingApproval? target = null;

            if (!string.IsNullOrEmpty(targetKey))
            {
                _pendingApprovals.TryGetValue(targetKey, out target);
            }
            else
            {
                target = _pendingApprovals.Values
                    .Where(p => p.Result == null)
                    .OrderBy(p => p.CreatedAt)
                    .FirstOrDefault();
            }

            if (target == null)
            {
                _logger.LogInformation("Approval response received but no pending approvals");
                return true;
            }

            target.Result = new ApprovalResult
            {
                Approved = isApproved.Value,
                RespondedBy = message.UserId,
                RespondedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Approval {Key} -> {Decision} by {User}",
                target.ApprovalKey,
                isApproved.Value ? "APPROVED" : "DENIED",
                message.UserId);
        }

        return true;
    }

    private Task<string> ProcessAudio(InboundMessage message, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(message.AudioFilePath))
        {
            _logger.LogInformation("Audio STT not yet configured, storing raw audio: {Path}", message.AudioFilePath);
            return Task.FromResult($"[audio:{message.AudioFilePath}]");
        }

        return Task.FromResult("[audio:download_failed]");
    }

    private async Task DispatchPendingNotifications(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_brokerApiUrl}/api/v1/high-level/line/notifications/pending?limit=10", ct);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("notifications", out var notifications) ||
                notifications.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var notification in notifications.EnumerateArray())
            {
                var notificationId = notification.TryGetProperty("notificationId", out var idProp) ? idProp.GetString() : null;
                var userId = notification.TryGetProperty("userId", out var userProp) ? userProp.GetString() : null;
                var title = notification.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
                var body = notification.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(notificationId) || string.IsNullOrWhiteSpace(userId))
                {
                    continue;
                }

                var message = string.IsNullOrWhiteSpace(title) ? body : $"{title}\n\n{body}";
                if (message.Length > 4900)
                {
                    message = message[..4900];
                }

                var (success, error) = await _lineApi.PushTextMessageAsync(userId, message, ct);
                await CompleteNotification(notificationId, success ? "sent" : "failed", error, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch pending LINE notifications.");
        }
    }

    private async Task CompleteNotification(string notificationId, string status, string? error, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var requestBody = JsonSerializer.Serialize(new
                {
                    notification_id = notificationId,
                    status,
                    error
                });
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_brokerApiUrl}/api/v1/high-level/line/notifications/complete", content, ct);

                if (response.IsSuccessStatusCode)
                    return;

                _logger.LogWarning(
                    "CompleteNotification failed for {NotificationId}: HTTP {StatusCode} (attempt {Attempt})",
                    notificationId, (int)response.StatusCode, attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CompleteNotification exception for {NotificationId} (attempt {Attempt})",
                    notificationId, attempt + 1);
            }

            if (attempt == 0)
                await Task.Delay(2000, ct);
        }
    }
}

public class PendingApproval
{
    public string ApprovalKey { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public ApprovalResult? Result { get; set; }
}

public class ApprovalResult
{
    public bool Approved { get; set; }
    public string RespondedBy { get; set; } = "";
    public DateTime RespondedAt { get; set; }
}
