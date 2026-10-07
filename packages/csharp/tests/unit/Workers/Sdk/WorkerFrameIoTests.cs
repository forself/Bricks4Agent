using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CacheProtocol;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerSdk;

namespace Unit.Tests.Workers.Sdk;

/// <summary>
/// WorkerHost 的連線讀寫：
/// - 一次讀取收到兩個以上的 frame 時，後續 frame 留給下一次讀取，不會被丟掉；
/// - 分派結果、心跳與狀態回覆從不同工作寫入同一條連線，寫入互斥，frame 不會交錯損毀。
/// </summary>
public sealed class WorkerFrameIoTests
{
    private static byte[] Frame(byte opCode, string payload)
        => FrameCodec.Encode(opCode, Encoding.UTF8.GetBytes(payload));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    [Fact]
    public async Task Reader_TwoFramesInOneRead_ReturnsBothInOrder()
    {
        var bytes = Concat(Frame(OpCodes.WORKER_EXECUTE, "first"), Frame(OpCodes.WORKER_EXECUTE, "second"));
        var reader = new FrameReader(new MemoryStream(bytes));

        var first = await reader.ReadFrameAsync(CancellationToken.None);
        reader.BufferedBytes.Should().BeGreaterThan(0, "the second frame arrived with the first read");
        var second = await reader.ReadFrameAsync(CancellationToken.None);

        Encoding.UTF8.GetString(first.Payload.Span).Should().Be("first");
        Encoding.UTF8.GetString(second.Payload.Span).Should().Be("second");
        second.OpCode.Should().Be(OpCodes.WORKER_EXECUTE);
        reader.BufferedBytes.Should().Be(0);
        await FluentActions.Awaiting(() => reader.ReadFrameAsync(CancellationToken.None)).Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Reader_LargeFrameAfterASmallOneInTheSameBuffer_IsReadCompletely()
    {
        var large = new string('L', 20_000);
        var bytes = Concat(Frame(OpCodes.PONG, ""), Frame(OpCodes.WORKER_EXECUTE, large), Frame(OpCodes.WORKER_STATUS, "tail"));
        var reader = new FrameReader(new MemoryStream(bytes));

        (await reader.ReadFrameAsync(CancellationToken.None)).OpCode.Should().Be(OpCodes.PONG);
        Encoding.UTF8.GetString((await reader.ReadFrameAsync(CancellationToken.None)).Payload.Span).Should().Be(large);
        Encoding.UTF8.GetString((await reader.ReadFrameAsync(CancellationToken.None)).Payload.Span).Should().Be("tail");
    }

    [Fact]
    public async Task Reader_FramesSplitIntoSingleBytes_AreReassembled()
    {
        var bytes = Concat(Frame(OpCodes.WORKER_EXECUTE, "a"), Frame(OpCodes.PONG, ""), Frame(OpCodes.WORKER_EXECUTE, "bc"));
        var reader = new FrameReader(new TrickleStream(bytes, chunk: 1));

        var payloads = new List<string>();
        for (var i = 0; i < 3; i++)
            payloads.Add(Encoding.UTF8.GetString((await reader.ReadFrameAsync(CancellationToken.None)).Payload.Span));

        payloads.Should().Equal("a", "", "bc");
    }

    [Fact]
    public async Task Writer_ConcurrentResultsAndHeartbeats_DoNotInterleave()
    {
        // 每次只寫入幾個位元組並讓出執行緒：沒有寫入鎖時，並行的寫入會交錯成損毀的 frame。
        var sink = new ChunkingSink(chunk: 7);
        var writer = new FrameWriter(sink);
        var results = Enumerable.Range(0, 20)
            .Select(i => JsonSerializer.Serialize(new { request_id = $"req_{i}", success = true, result_payload = new string((char)('a' + i % 26), 3000) }))
            .ToList();

        var writes = results.Select(result => writer.WriteFrameAsync(Frame(OpCodes.WORKER_RESULT, result), CancellationToken.None))
            .Concat(Enumerable.Range(0, 20).Select(_ => writer.WriteFrameAsync(FrameCodec.EncodeEmpty(OpCodes.PING), CancellationToken.None)))
            .ToList();
        await Task.WhenAll(writes);

        var reader = new FrameReader(new MemoryStream(sink.ToArray()));
        var received = new List<(byte OpCode, string Payload)>();
        for (var i = 0; i < 40; i++)
        {
            var frame = await reader.ReadFrameAsync(CancellationToken.None);
            received.Add((frame.OpCode, Encoding.UTF8.GetString(frame.Payload.Span)));
        }

        received.Count(frame => frame.OpCode == OpCodes.PING).Should().Be(20);
        received.Where(frame => frame.OpCode == OpCodes.WORKER_RESULT).Select(frame => frame.Payload)
            .Should().BeEquivalentTo(results);
        reader.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public async Task WorkerHost_TwoRequestsInOneWrite_BothAnswered_AndEveryFrameIsIntact()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var host = new WorkerHost(new WorkerHostOptions
        {
            BrokerHost = "127.0.0.1",
            BrokerPort = port,
            WorkerId = "wkr_frame_test",
            HeartbeatIntervalSeconds = 1,
            AutoReconnect = false,
        }, NullLogger<WorkerHost>.Instance);
        host.RegisterHandler(new DelayedEchoHandler(TimeSpan.FromMilliseconds(2500)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var running = host.RunAsync(cts.Token);

        using var broker = await listener.AcceptTcpClientAsync(cts.Token);
        var stream = broker.GetStream();
        var reader = new FrameReader(stream);

        var register = await reader.ReadFrameAsync(cts.Token);
        register.OpCode.Should().Be(OpCodes.WORKER_REGISTER);
        await stream.WriteAsync(Frame(OpCodes.WORKER_REGISTER_ACK, """{"ok":true,"worker_id":"wkr_frame_test"}"""), cts.Token);

        // 兩個分派放在同一次寫入：worker 必須兩個都處理。
        var executeA = JsonSerializer.Serialize(new { request_id = "req_a", capability_id = "test.echo", route = "echo", payload = "{}", scope = "{}" });
        var executeB = JsonSerializer.Serialize(new { request_id = "req_b", capability_id = "test.echo", route = "echo", payload = "{}", scope = "{}" });
        await stream.WriteAsync(Concat(Frame(OpCodes.WORKER_EXECUTE, executeA), Frame(OpCodes.WORKER_EXECUTE, executeB)), cts.Token);

        var answered = new HashSet<string>();
        var pings = 0;
        while (answered.Count < 2)
        {
            var frame = await reader.ReadFrameAsync(cts.Token);
            if (frame.OpCode == OpCodes.PING)
            {
                pings++;
                continue;
            }

            frame.OpCode.Should().Be(OpCodes.WORKER_RESULT);
            using var result = JsonDocument.Parse(frame.Payload);
            result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            answered.Add(result.RootElement.GetProperty("request_id").GetString()!);
        }

        answered.Should().BeEquivalentTo("req_a", "req_b");
        pings.Should().BeGreaterThan(0, "heartbeats were written while the requests ran");

        cts.Cancel();
        await running;
    }

    private sealed class DelayedEchoHandler : ICapabilityHandler
    {
        private readonly TimeSpan _delay;

        public DelayedEchoHandler(TimeSpan delay) => _delay = delay;

        public string CapabilityId => "test.echo";

        public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
            string requestId, string route, string payload, string scope, CancellationToken ct)
        {
            await Task.Delay(_delay, ct);
            return (true, JsonSerializer.Serialize(new { echo = requestId, padding = new string('p', 50_000) }), null);
        }
    }

    /// <summary>每次讀取最多交出 chunk 個位元組的唯讀 stream。</summary>
    private sealed class TrickleStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _position;

        public TrickleStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = chunk;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var count = Math.Min(Math.Min(_chunk, buffer.Length), _data.Length - _position);
            _data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>每次只寫入 chunk 個位元組、每段之間讓出執行緒的唯寫 stream（模擬分段送出的 socket）。</summary>
    private sealed class ChunkingSink : Stream
    {
        private readonly int _chunk;
        private readonly List<byte> _written = new();
        private readonly object _sync = new();

        public ChunkingSink(int chunk) => _chunk = chunk;

        public byte[] ToArray()
        {
            lock (_sync)
                return _written.ToArray();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            for (var offset = 0; offset < buffer.Length; offset += _chunk)
            {
                var piece = buffer.Slice(offset, Math.Min(_chunk, buffer.Length - offset)).ToArray();
                lock (_sync)
                    _written.AddRange(piece);
                await Task.Yield();
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => ToArray().Length;
        public override long Position { get => Length; set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
