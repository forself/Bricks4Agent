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
using WorkerSdk;

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
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            worker_id = "file-wkr-unauth",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2
        });
        var frame = FrameCodec.Encode(OpCodes.WORKER_REGISTER, payload);
        await client.GetStream().WriteAsync(frame);
        await client.GetStream().FlushAsync();

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (_, ackPayload) = await ReceiveFrameAsync(client, readCts.Token);
        var ack = JsonSerializer.Deserialize<WorkerRegisterAckProbe>(ackPayload.Span, JsonOptions);

        ack.Should().NotBeNull();
        ack!.Ok.Should().BeFalse();
        ack.Error.Should().Contain("WorkerAuth:Enforce=true")
            .And.Contain("key_id").And.Contain("timestamp").And.Contain("nonce").And.Contain("signature");
        registry.GetAllWorkers().Should().BeEmpty();

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_EnforceOff_WithoutCredentials_RegistersWorker()
    {
        // WorkerHost 在沒有憑證時送出的內容：key_id 與 signature 為空字串，timestamp／nonce 仍帶。
        var (client, session, registry) = CreateSession(enforce: false);
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-dev",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2,
            key_id = "",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            nonce = Guid.NewGuid().ToString("N"),
            signature = ""
        });

        ack.Ok.Should().BeTrue();
        ack.Error.Should().BeNull();
        registry.GetAllWorkers().Should().ContainSingle(item => item.WorkerId == "file-wkr-dev");

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_EnforceOff_WithoutAnyAuthFields_RegistersWorker()
    {
        var (client, session, registry) = CreateSession(enforce: false);
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-bare",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2
        });

        ack.Ok.Should().BeTrue();
        registry.GetAllWorkers().Should().ContainSingle(item => item.WorkerId == "file-wkr-bare");

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_EnforceOff_UnverifiableSignature_StillRegisters()
    {
        // Enforce=false 時不驗簽（既有行為）：帶著 appsettings 佔位憑證的 worker 照常註冊。
        var (client, session, registry) = CreateSession(enforce: false);
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-placeholder",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2,
            key_id = "REPLACE_WITH_FILE_WORKER_KEY_ID",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            nonce = Guid.NewGuid().ToString("N"),
            signature = "not-a-valid-signature"
        });

        ack.Ok.Should().BeTrue();
        registry.GetAllWorkers().Should().ContainSingle(item => item.WorkerId == "file-wkr-placeholder");

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessAsync_WorkerRegister_WithoutWorkerType_IsRejected(bool enforce)
    {
        var (client, session, registry) = CreateSession(enforce);
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-notype",
            capabilities = new[] { "file.read" },
            max_concurrent = 2
        });

        ack.Ok.Should().BeFalse();
        ack.Error.Should().Be("Missing worker type");
        registry.GetAllWorkers().Should().BeEmpty();

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_EnforceOn_EmptyKeyIdAndSignature_IsRejectedNamingOnlyMissingFields()
    {
        var (client, session, registry) = CreateSession();
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-nocred",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2,
            key_id = "",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            nonce = Guid.NewGuid().ToString("N"),
            signature = ""
        });

        ack.Ok.Should().BeFalse();
        ack.Error.Should().EndWith("missing: key_id, signature");
        registry.GetAllWorkers().Should().BeEmpty();

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_EnforceOn_InvalidSignature_IsRejected()
    {
        var (client, session, registry) = CreateSession();
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        // 用錯的 shared secret 簽：格式正確但驗不過。
        var signature = BuildAuthService().SignWorkerRegister(
            "file-worker",
            "file-v1",
            "wrong-secret",
            "file-wkr-forged",
            ["file.read"],
            2,
            timestamp,
            nonce);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-forged",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2,
            key_id = "file-v1",
            timestamp = timestamp.ToString("O"),
            nonce,
            signature
        });

        ack.Ok.Should().BeFalse();
        ack.Error.Should().Be("worker signature invalid");
        registry.GetAllWorkers().Should().BeEmpty();

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_EnforceOn_InvalidTimestamp_IsRejected()
    {
        var (client, session, registry) = CreateSession();
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);

        var ack = await RegisterAsync(client, new
        {
            worker_id = "file-wkr-badtime",
            worker_type = "file-worker",
            capabilities = new[] { "file.read" },
            max_concurrent = 2,
            key_id = "file-v1",
            timestamp = "not-a-timestamp",
            nonce = Guid.NewGuid().ToString("N"),
            signature = "anything"
        });

        ack.Ok.Should().BeFalse();
        ack.Error.Should().Be("Invalid worker authentication timestamp");
        registry.GetAllWorkers().Should().BeEmpty();

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WorkerHost_StartedWithAllowUnauthenticated_RegistersOnlyWhenBrokerDoesNotEnforce(
        bool enforce, bool expectRegistered)
    {
        // 兩端一起驗：worker 端用 WorkerAuthSettings 的 -AllowUnauthenticated 結果，broker 端用真的 WorkerSession。
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var auth = WorkerAuthSettings.Resolve(
            allowUnauthenticated: true,
            workerType: null,
            defaultWorkerType: "file-worker",
            keyId: "configured-key",
            sharedSecret: "configured-secret");

        var host = new WorkerHost(new WorkerHostOptions
        {
            BrokerHost = "127.0.0.1",
            BrokerPort = port,
            WorkerId = "file-wkr-host",
            HeartbeatIntervalSeconds = 1,
            AutoReconnect = false,
            WorkerType = auth.WorkerType,
            WorkerAuthKeyId = auth.KeyId,
            WorkerAuthSharedSecret = auth.SharedSecret
        }, NullLogger<WorkerHost>.Instance);
        host.RegisterHandler(new NoopHandler());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var running = host.RunAsync(cts.Token);

        using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
        var registry = new WorkerRegistry(NullLogger<WorkerRegistry>.Instance);
        var session = new WorkerSession(
            new WorkerConnection(serverClient, NullLogger.Instance),
            registry,
            BuildAuthService(enforce),
            NullLogger.Instance);
        var processTask = session.ProcessAsync(cts.Token);

        if (expectRegistered)
        {
            // 註冊成功後 worker 留在接收迴圈，直到取消。
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (registry.GetAllWorkers().Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20, cts.Token);
            registry.GetAllWorkers().Should().ContainSingle(item => item.WorkerId == "file-wkr-host");
            running.IsCompleted.Should().BeFalse();
        }
        else
        {
            // 被拒後（AutoReconnect=false）worker 直接結束，不會留在 registry。
            await running.WaitAsync(TimeSpan.FromSeconds(10));
            registry.GetAllWorkers().Should().BeEmpty();
        }

        cts.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_WorkerRegister_WithSignature_RegistersWorker()
    {
        var (client, session, registry) = CreateSession();
        using var processCts = new CancellationTokenSource();
        var processTask = session.ProcessAsync(processCts.Token);
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
        await client.GetStream().WriteAsync(frame);
        await client.GetStream().FlushAsync();

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (_, ackPayload) = await ReceiveFrameAsync(client, readCts.Token);
        var ack = JsonSerializer.Deserialize<WorkerRegisterAckProbe>(ackPayload.Span, JsonOptions);

        ack.Should().NotBeNull();
        ack!.Ok.Should().BeTrue();
        registry.GetAllWorkers().Should().ContainSingle(item => item.WorkerId == "file-wkr-auth");

        processCts.Cancel();
        await processTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<WorkerRegisterAckProbe> RegisterAsync(TcpClient client, object registerMessage)
    {
        var frame = FrameCodec.Encode(OpCodes.WORKER_REGISTER, JsonSerializer.SerializeToUtf8Bytes(registerMessage));
        await client.GetStream().WriteAsync(frame);
        await client.GetStream().FlushAsync();

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (opCode, ackPayload) = await ReceiveFrameAsync(client, readCts.Token);
        opCode.Should().Be(OpCodes.WORKER_REGISTER_ACK);
        var ack = JsonSerializer.Deserialize<WorkerRegisterAckProbe>(ackPayload.Span, JsonOptions);
        ack.Should().NotBeNull();
        return ack!;
    }

    private (TcpClient Client, WorkerSession Session, WorkerRegistry Registry) CreateSession(bool enforce = true)
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
        var session = new WorkerSession(connection, registry, BuildAuthService(enforce), NullLogger.Instance);
        return (client, session, registry);
    }

    private static WorkerIdentityAuthService BuildAuthService(bool enforce = true)
    {
        var options = new WorkerIdentityAuthOptions
        {
            Enforce = enforce,
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

    private sealed class NoopHandler : ICapabilityHandler
    {
        public string CapabilityId => "file.read";

        public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
            string requestId, string route, string payload, string scope, CancellationToken ct)
            => Task.FromResult<(bool Success, string? ResultPayload, string? Error)>((true, "{}", null));
    }

    private sealed class WorkerRegisterAckProbe
    {
        public bool Ok { get; set; }
        public string? WorkerId { get; set; }
        public string? Error { get; set; }
    }
}
