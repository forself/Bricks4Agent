using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using CacheProtocol;
using FluentAssertions;
using FunctionPool.Dispatch;
using FunctionPool.Models;
using FunctionPool.Network;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Unit.Tests.Transport;

public class TransportDispatchPreferenceTests : IAsyncDisposable
{
    private readonly List<(TcpListener Listener, TcpClient Client, TcpClient ServerClient)> _tcpPairs = [];

    [Fact]
    public async Task FallbackDispatcher_prefers_registered_transport_worker_before_broker_fallback()
    {
        var registry = new WorkerRegistry(NullLogger<WorkerRegistry>.Instance);
        var (connection, remoteClient) = await CreateConnectionPairAsync("wkr_transport");
        registry.Register(new WorkerInfo
        {
            WorkerId = "wkr_transport",
            Capabilities = ["transport.query"],
            MaxConcurrent = 2
        }, connection);

        var fallback = Substitute.For<IExecutionDispatcher>();
        fallback.DispatchAsync(Arg.Any<ApprovedRequest>())
            .Returns(Task.FromResult(ExecutionResult.Fail("fallback", "fallback should not be used")));

        var dispatcher = new FallbackDispatcher(
            new PoolDispatcher(
                registry,
                new PoolConfig { DispatchTimeout = TimeSpan.FromSeconds(3), MaxRetries = 0 },
                NullLogger<PoolDispatcher>.Instance),
            fallback,
            _ => true,
            NullLogger<FallbackDispatcher>.Instance);

        var request = new ApprovedRequest
        {
            RequestId = "req_transport_001",
            CapabilityId = "transport.query",
            Route = "transport_query",
            Payload = """{"transport_mode":"rail","user_query":"板橋到高雄"}""",
            Scope = "{}",
            TraceId = "trace_transport_001"
        };

        var workerSide = ObserveDispatchAndCompleteAsync(connection, remoteClient, request.RequestId);

        var result = await dispatcher.DispatchAsync(request);

        result.Success.Should().BeTrue();
        result.ResultPayload.Should().Contain("from transport worker");
        await fallback.DidNotReceive().DispatchAsync(Arg.Any<ApprovedRequest>());
        await workerSide;
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

    private async Task ObserveDispatchAndCompleteAsync(WorkerConnection connection, TcpClient remoteClient, string requestId)
    {
        var command = await ReadWorkerExecuteCommandAsync(remoteClient);

        command.RequestId.Should().Be(requestId);
        command.CapabilityId.Should().Be("transport.query");
        command.Route.Should().Be("transport_query");
        command.Payload.Should().Contain("板橋");

        var workerPayload = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new WorkerResultMessage
            {
                RequestId = requestId,
                Success = true,
                ResultPayload = """{"resultType":"final_answer","answer":"from transport worker"}"""
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }));
        connection.CompleteRequest(requestId, OpCodes.WORKER_RESULT, workerPayload).Should().BeTrue();
    }

    private async Task<WorkerExecuteCommand> ReadWorkerExecuteCommandAsync(TcpClient remoteClient)
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
