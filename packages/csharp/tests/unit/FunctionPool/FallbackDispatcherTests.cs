using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using CacheProtocol;
using FunctionPool.Dispatch;
using FunctionPool.Models;
using FunctionPool.Network;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.FunctionPool;

/// <summary>
/// FallbackDispatcher falls back to the in-process dispatcher only when no worker can take the request
/// (no worker registered, transport failure or timeout). A result the worker answered, a refusal included,
/// is final and is never handed to the in-process dispatcher.
/// </summary>
public sealed class FallbackDispatcherTests : IAsyncDisposable
{
    private const string Capability = "file.search_name";
    private const string Route = "search_files";

    private readonly List<(TcpListener Listener, TcpClient Client, TcpClient ServerClient)> _tcpPairs = [];

    [Fact]
    public async Task WorkerRefusal_IsReturnedWithoutFallingBack()
    {
        var (registry, connection, remoteClient) = await RegisterWorkerAsync();
        var fallback = FallbackReturning(ExecutionResult.Ok("fallback", """{"matches":["from-fallback"]}"""));
        var dispatcher = NewDispatcher(registry, fallback, TimeSpan.FromSeconds(3));
        var request = NewRequest("req_refused");

        var workerSide = AnswerAsync(connection, remoteClient, request.RequestId, OpCodes.WORKER_RESULT,
            new WorkerResultMessage { RequestId = request.RequestId, Success = false, Error = "refused by worker" });

        var result = await dispatcher.DispatchAsync(request);
        await workerSide;

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("refused by worker");
        result.AnsweredByWorker.Should().BeTrue();
        result.ResultPayload.Should().BeNull();
        await fallback.DidNotReceive().DispatchAsync(Arg.Any<ApprovedRequest>());
    }

    [Fact]
    public async Task UnreadableWorkerReply_IsFinalToo()
    {
        var (registry, connection, remoteClient) = await RegisterWorkerAsync();
        var fallback = FallbackReturning(ExecutionResult.Ok("fallback", "{}"));
        var dispatcher = NewDispatcher(registry, fallback, TimeSpan.FromSeconds(3));
        var request = NewRequest("req_unreadable");

        var workerSide = AnswerAsync(connection, remoteClient, request.RequestId, OpCodes.WORKER_STATUS, reply: null);

        var result = await dispatcher.DispatchAsync(request);
        await workerSide;

        result.Success.Should().BeFalse();
        result.AnsweredByWorker.Should().BeTrue();
        await fallback.DidNotReceive().DispatchAsync(Arg.Any<ApprovedRequest>());
    }

    [Fact]
    public async Task WorkerSuccess_IsReturnedAsIs()
    {
        var (registry, connection, remoteClient) = await RegisterWorkerAsync();
        var fallback = FallbackReturning(ExecutionResult.Ok("fallback", "{}"));
        var dispatcher = NewDispatcher(registry, fallback, TimeSpan.FromSeconds(3));
        var request = NewRequest("req_ok");

        var workerSide = AnswerAsync(connection, remoteClient, request.RequestId, OpCodes.WORKER_RESULT,
            new WorkerResultMessage { RequestId = request.RequestId, Success = true, ResultPayload = """{"matches":["a.txt"]}""" });

        var result = await dispatcher.DispatchAsync(request);
        await workerSide;

        result.Success.Should().BeTrue();
        result.ResultPayload.Should().Contain("a.txt");
        await fallback.DidNotReceive().DispatchAsync(Arg.Any<ApprovedRequest>());
    }

    [Fact]
    public async Task NoRegisteredWorker_FallsBack()
    {
        var registry = new WorkerRegistry(NullLogger<WorkerRegistry>.Instance);
        var fallback = FallbackReturning(ExecutionResult.Ok("fallback", """{"matches":["from-fallback"]}"""));
        var dispatcher = NewDispatcher(registry, fallback, TimeSpan.FromSeconds(3));

        var result = await dispatcher.DispatchAsync(NewRequest("req_no_worker"));

        result.Success.Should().BeTrue();
        result.ResultPayload.Should().Contain("from-fallback");
        await fallback.Received(1).DispatchAsync(Arg.Any<ApprovedRequest>());
    }

    [Fact]
    public async Task WorkerTimeout_FallsBack()
    {
        var (registry, _, remoteClient) = await RegisterWorkerAsync();
        var fallback = FallbackReturning(ExecutionResult.Ok("fallback", """{"matches":["from-fallback"]}"""));
        var dispatcher = NewDispatcher(registry, fallback, TimeSpan.FromMilliseconds(300));
        var request = NewRequest("req_timeout");

        // The worker receives the request but never answers.
        var observed = ReadWorkerExecuteCommandAsync(remoteClient);

        var result = await dispatcher.DispatchAsync(request);
        (await observed).RequestId.Should().Be(request.RequestId);

        result.Success.Should().BeTrue();
        result.ResultPayload.Should().Contain("from-fallback");
        result.AnsweredByWorker.Should().BeFalse();
        await fallback.Received(1).DispatchAsync(Arg.Any<ApprovedRequest>());
    }

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

    private static FallbackDispatcher NewDispatcher(IWorkerRegistry registry, IExecutionDispatcher fallback, TimeSpan timeout)
        => new(
            new PoolDispatcher(
                registry,
                new PoolConfig { DispatchTimeout = timeout, MaxRetries = 0 },
                NullLogger<PoolDispatcher>.Instance),
            fallback,
            route => route == Route,
            NullLogger<FallbackDispatcher>.Instance);

    private static IExecutionDispatcher FallbackReturning(ExecutionResult result)
    {
        var fallback = Substitute.For<IExecutionDispatcher>();
        fallback.DispatchAsync(Arg.Any<ApprovedRequest>()).Returns(Task.FromResult(result));
        return fallback;
    }

    private static ApprovedRequest NewRequest(string requestId) => new()
    {
        RequestId = requestId,
        CapabilityId = Capability,
        Route = Route,
        Payload = """{"route":"search_files","args":{"pattern":"*.txt"}}""",
        Scope = "{}",
        TraceId = "trace_" + requestId
    };

    private async Task<(WorkerRegistry Registry, WorkerConnection Connection, TcpClient RemoteClient)> RegisterWorkerAsync()
    {
        var registry = new WorkerRegistry(NullLogger<WorkerRegistry>.Instance);
        var (connection, remoteClient) = await CreateConnectionPairAsync("wkr_file");
        registry.Register(new WorkerInfo
        {
            WorkerId = "wkr_file",
            Capabilities = [Capability],
            MaxConcurrent = 2
        }, connection);
        return (registry, connection, remoteClient);
    }

    private static async Task AnswerAsync(
        WorkerConnection connection, TcpClient remoteClient, string requestId, byte opCode, WorkerResultMessage? reply)
    {
        var command = await ReadWorkerExecuteCommandAsync(remoteClient);
        command.RequestId.Should().Be(requestId);

        var payload = reply == null
            ? Encoding.UTF8.GetBytes("{}")
            : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }));
        connection.CompleteRequest(requestId, opCode, payload).Should().BeTrue();
    }

    private static async Task<WorkerExecuteCommand> ReadWorkerExecuteCommandAsync(TcpClient remoteClient)
    {
        var stream = remoteClient.GetStream();
        var buffer = new byte[4096];
        var filled = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled));
            bytesRead.Should().BeGreaterThan(0);
            filled += bytesRead;

            if (FrameCodec.TryParse(buffer.AsSpan(0, filled), out var frame))
            {
                frame.OpCode.Should().Be(OpCodes.WORKER_EXECUTE);
                var json = Encoding.UTF8.GetString(frame.Payload.Span);
                var command = JsonSerializer.Deserialize<WorkerExecuteCommand>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    PropertyNameCaseInsensitive = true
                });

                command.Should().NotBeNull();
                return command!;
            }
        }
    }

    private async Task<(WorkerConnection Connection, TcpClient RemoteClient)> CreateConnectionPairAsync(string workerId)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await listener.AcceptTcpClientAsync();

        _tcpPairs.Add((listener, client, serverClient));

        var connection = new WorkerConnection(serverClient, NullLogger.Instance)
        {
            WorkerId = workerId
        };

        return (connection, client);
    }
}
