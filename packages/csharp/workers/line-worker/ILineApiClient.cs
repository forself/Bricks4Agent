namespace LineWorker;

public interface ILineApiClient
{
    Task<(bool Success, string? Error)> PushTextMessageAsync(
        string recipientId,
        string text,
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> PushAudioMessageAsync(
        string recipientId,
        string audioUrl,
        int durationMs,
        CancellationToken ct = default);
}
