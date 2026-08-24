using System.Text.Json;
using WorkerSdk;

namespace LineWorker.Handlers;

/// <summary>
/// line.audio.send 能力處理器 — 發送語音訊息至 LINE
///
/// 流程：
/// 1. 接收文字 → TTS 轉音檔（或直接提供音檔 URL）
/// 2. 透過 LINE Push API 發送 audio message
///
/// payload:
///   to           (string, optional) — 接收者，省略則用預設
///   text         (string, optional) — TTS 文字（與 audio_url 二擇一）
///   audio_url    (string, optional) — 音檔 URL（與 text 二擇一）
///   duration_ms  (int, optional)    — 音訊時長毫秒，預設 5000
/// </summary>
public class SendAudioHandler : ICapabilityHandler
{
    private readonly ILineApiClient _lineApi;
    private readonly string _defaultRecipientId;
    private readonly ILineOutboundRateLimiter _rateLimiter;

    public string CapabilityId => "line.audio.send";

    public SendAudioHandler(
        ILineApiClient lineApi,
        string defaultRecipientId,
        ILineOutboundRateLimiter? rateLimiter = null)
    {
        _lineApi = lineApi;
        _defaultRecipientId = defaultRecipientId;
        _rateLimiter = rateLimiter ?? new LineOutboundRateLimiter();
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

            var durationMs = root.TryGetProperty("duration_ms", out var dur)
                ? dur.GetInt32()
                : 5000;

            string? audioUrl = null;

            // 優先使用提供的 audio_url
            if (root.TryGetProperty("audio_url", out var urlProp))
            {
                audioUrl = urlProp.GetString();
            }
            else if (root.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString() ?? "";
                if (!_rateLimiter.TryAcquire(to, CapabilityId, out var fallbackRetryAfter))
                    return (false, null, $"Outbound LINE rate limit exceeded for {CapabilityId}; retry after {FormatRetryAfter(fallbackRetryAfter)}.");

                // TODO: 對接 TTS 服務（Azure / Google / OpenAI）
                // ttsResult = await _ttsService.SynthesizeAsync(text, ct);
                // audioUrl = ttsResult.Url;
                // durationMs = ttsResult.DurationMs;

                // 暫時 fallback：直接發文字訊息
                var (sent, error) = await _lineApi.PushTextMessageAsync(
                    to, $"\U0001f50a {text}", ct);

                if (!sent)
                    return (false, null, error);

                var fallbackResult = JsonSerializer.Serialize(new
                {
                    sent = true,
                    mode = "text_fallback",
                    to,
                    reason = "TTS not configured, sent as text message"
                });

                return (true, fallbackResult, null);
            }
            else
            {
                return (false, null, "Either 'text' or 'audio_url' must be provided.");
            }

            if (string.IsNullOrEmpty(audioUrl))
                return (false, null, "Failed to resolve audio URL.");

            if (!_rateLimiter.TryAcquire(to, CapabilityId, out var retryAfter))
                return (false, null, $"Outbound LINE rate limit exceeded for {CapabilityId}; retry after {FormatRetryAfter(retryAfter)}.");

            var (success, sendError) = await _lineApi.PushAudioMessageAsync(to, audioUrl, durationMs, ct);

            if (!success)
                return (false, null, sendError);

            var result = JsonSerializer.Serialize(new
            {
                sent = true,
                mode = "audio",
                to,
                durationMs
            });

            return (true, result, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"SendAudio error: {ex.Message}");
        }
    }

    private static string FormatRetryAfter(TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        return $"{seconds}s";
    }
}
