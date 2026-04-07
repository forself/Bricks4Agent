using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BrokerCore.Services;
using CacheProtocol;
using FluentAssertions;
using FunctionPool.Models;
using FunctionPool.Network;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.FunctionPool;

public class WorkerSessionAuthTests : IAsyncDisposable
{
    private readonly List<(TcpListener Listener, TcpClient Client, TcpClient ServerClient)> _tcpPairs = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var (listener, client, serverClient) in _tcpPairs)
        {
            try { client.Dispose(); } catch { }
            try { serverClient.Dispose(); } catch { }
            try { listener.Stop(); } catch { }
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_WithoutSignature_IsRejected()
    {
        var (client, session, registry) = CreateSession();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processTask = session.ProcessAsync(cts.Token);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            worker_id = "file-wkr-unauth",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2
        });
        var frame = FrameCodec.Encode(OpCodes.WORKER_REGISTER, payload);
        await client.GetStream().WriteAsync(frame, cts.Token);
        await client.GetStream().FlushAsync(cts.Token);

        var (_, ackPayload) = await ReceiveFrameAsync(client, cts.Token);
        var ack = JsonSerializer.Deserialize<WorkerRegisterAckProbe>(ackPayload.Span, JsonOptions);

        ack.Should().NotBeNull();
        ack!.Ok.Should().BeFalse();
        registry.GetAllWorkers().Should().BeEmpty();

        cts.Cancel();
        await processTask;
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_WithSignature_RegistersWorker()
    {
        var (client, session, registry) = CreateSession();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processTask = session.ProcessAsync(cts.Token);
        var authService = BuildAuthService();
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = authService.SignWorkerRegister(
            "file-worker",
            "file-v1",
            "file-secret",
            "file-wkr-auth",
            ["file.read"],
            2,
            timestamp,
            nonce);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            worker_id = "file-wkr-auth",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2,
            key_id = "file-v1",
            timestamp = timestamp.ToString("O"),
            nonce,
            signature
        });
        var frame = FrameCodec.Encode(OpCodes.WORKER_REGISTER, payload);
        await client.GetStream().WriteAsync(frame, cts.Token);
        await client.GetStream().FlushAsync(cts.Token);

        var (_, ackPayload) = await ReceiveFrameAsync(client, cts.Token);
        var ack = JsonSerializer.Deserialize<WorkerRegisterAckProbe>(ackPayload.Span, JsonOptions);

        ack.Should().NotBeNull();
        ack!.Ok.Should().BeTrue();
        registry.GetAllWorkers().Should().ContainSingle(item => item.WorkerId == "file-wkr-auth");

        cts.Cancel();
        await processTask;
    }

    private (TcpClient Client, WorkerSession Session, WorkerRegistry Registry) CreateSession()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        _tcpPairs.Add((listener, client, serverClient));

        var connection = new WorkerConnection(serverClient, NullLogger.Instance);
        var registry = new WorkerRegistry(NullLogger<WorkerRegistry>.Instance);
        var session = new WorkerSession(connection, registry, BuildAuthService(), NullLogger.Instance);
        return (client, session, registry);
    }

    private static WorkerIdentityAuthService BuildAuthService()
    {
        var options = new WorkerIdentityAuthOptions
        {
            Enforce = true,
            ClockSkewSeconds = 300,
            Credentials =
            [
                new WorkerCredentialRecord
                {
                    WorkerType = "file-worker",
                    KeyId = "file-v1",
                    SharedSecret = "file-secret",
                    Status = "active"
                }
            ]
        };
        return new WorkerIdentityAuthService(options, new WorkerAuthNonceStore());
    }

    private static async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)> ReceiveFrameAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var buffer = new byte[4096];
        var filled = 0;

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), ct);
            bytesRead.Should().BeGreaterThan(0);
            filled += bytesRead;

            if (FrameCodec.TryParse(buffer.AsSpan(0, filled), out var frame))
            {
                var payload = new byte[frame.Payload.Length];
                frame.Payload.Span.CopyTo(payload);
                return (frame.OpCode, payload);
            }
        }

        throw new OperationCanceledException();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private sealed class WorkerRegisterAckProbe
    {
        public bool Ok { get; set; }
        public string? WorkerId { get; set; }
        public string? Error { get; set; }
    }
}
