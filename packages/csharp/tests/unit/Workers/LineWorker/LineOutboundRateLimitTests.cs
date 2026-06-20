using System.Text.Json;
using LineWorker;
using LineWorker.Handlers;

namespace Unit.Tests.Workers.LineWorker;

public class LineOutboundRateLimitTests
{
    [Fact]
    public async Task SendMessage_RateLimitsPerRecipientAndCapability()
    {
        var lineApi = new RecordingLineApiClient();
        var limiter = new LineOutboundRateLimiter(new LineOutboundRateLimitOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1)
        });
        var handler = new SendMessageHandler(lineApi, defaultRecipientId: "default-user", limiter);

        var first = await handler.ExecuteAsync(
            "req-1",
            "route",
            JsonSerializer.Serialize(new { to = "user-1", text = "hello" }),
            "scope",
            CancellationToken.None);
        var second = await handler.ExecuteAsync(
            "req-2",
            "route",
            JsonSerializer.Serialize(new { to = "user-1", text = "again" }),
            "scope",
            CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.Error.Should().Contain("rate limit");
        lineApi.TextMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAudio_RateLimitsIndependentlyFromMessageCapability()
    {
        var lineApi = new RecordingLineApiClient();
        var limiter = new LineOutboundRateLimiter(new LineOutboundRateLimitOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1)
        });
        var messageHandler = new SendMessageHandler(lineApi, defaultRecipientId: "default-user", limiter);
        var audioHandler = new SendAudioHandler(lineApi, defaultRecipientId: "default-user", limiter);

        var message = await messageHandler.ExecuteAsync(
            "req-1",
            "route",
            JsonSerializer.Serialize(new { to = "user-1", text = "hello" }),
            "scope",
            CancellationToken.None);
        var audio = await audioHandler.ExecuteAsync(
            "req-2",
            "route",
            JsonSerializer.Serialize(new { to = "user-1", audio_url = "https://example.test/a.m4a", duration_ms = 1000 }),
            "scope",
            CancellationToken.None);
        var secondAudio = await audioHandler.ExecuteAsync(
            "req-3",
            "route",
            JsonSerializer.Serialize(new { to = "user-1", audio_url = "https://example.test/b.m4a", duration_ms = 1000 }),
            "scope",
            CancellationToken.None);

        message.Success.Should().BeTrue();
        audio.Success.Should().BeTrue();
        secondAudio.Success.Should().BeFalse();
        secondAudio.Error.Should().Contain("rate limit");
        lineApi.TextMessages.Should().ContainSingle();
        lineApi.AudioMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task SendMessage_RateLimitsPerRecipient()
    {
        var lineApi = new RecordingLineApiClient();
        var limiter = new LineOutboundRateLimiter(new LineOutboundRateLimitOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1)
        });
        var handler = new SendMessageHandler(lineApi, defaultRecipientId: "default-user", limiter);

        var first = await handler.ExecuteAsync(
            "req-1",
            "route",
            JsonSerializer.Serialize(new { to = "user-1", text = "hello" }),
            "scope",
            CancellationToken.None);
        var otherRecipient = await handler.ExecuteAsync(
            "req-2",
            "route",
            JsonSerializer.Serialize(new { to = "user-2", text = "hello" }),
            "scope",
            CancellationToken.None);

        first.Success.Should().BeTrue();
        otherRecipient.Success.Should().BeTrue();
        lineApi.TextMessages.Should().HaveCount(2);
    }

    private sealed class RecordingLineApiClient : ILineApiClient
    {
        public List<(string To, string Text)> TextMessages { get; } = new();
        public List<(string To, string AudioUrl, int DurationMs)> AudioMessages { get; } = new();

        public Task<(bool Success, string? Error)> PushTextMessageAsync(
            string recipientId,
            string text,
            CancellationToken ct = default)
        {
            TextMessages.Add((recipientId, text));
            return Task.FromResult<(bool Success, string? Error)>((true, null));
        }

        public Task<(bool Success, string? Error)> PushAudioMessageAsync(
            string recipientId,
            string audioUrl,
            int durationMs,
            CancellationToken ct = default)
        {
            AudioMessages.Add((recipientId, audioUrl, durationMs));
            return Task.FromResult<(bool Success, string? Error)>((true, null));
        }
    }
}
