using System.Net;
using System.Net.Sockets;
using FunctionPool.Models;
using FunctionPool.Network;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.FunctionPool;

/// <summary>
/// WorkerRegistry unit tests covering registration, deregistration,
/// round-robin dispatch, active task tracking, and concurrency safety.
///
/// WorkerConnection requires a real TcpClient, so we create loopback
/// TCP connections for testing.
/// </summary>
public class WorkerRegistryTests : IAsyncDisposable
{
    private readonly WorkerRegistry _registry;
    private readonly ILogger<WorkerRegistry> _logger;
    private readonly List<(TcpListener Listener, TcpClient Client, TcpClient ServerClient)> _tcpPairs = new();

    public WorkerRegistryTests()
    {
        _logger = NullLogger<WorkerRegistry>.Instance;
        _registry = new WorkerRegistry(_logger);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (listener, client, serverClient) in _tcpPairs)
        {
            try { client.Dispose(); } catch { }
            try { serverClient.Dispose(); } catch { }
            try { listener.Stop(); } catch { }
        }
    }

    /// <summary>
    /// Create a real WorkerConnection backed by a loopback TCP pair.
    /// </summary>
    private WorkerConnection CreateConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();

        _tcpPairs.Add((listener, client, serverClient));

        return new WorkerConnection(serverClient, NullLogger.Instance);
    }

    private static WorkerInfo MakeWorkerInfo(
        string workerId, List<string> capabilities, int maxConcurrent = 4)
    {
        return new WorkerInfo
        {
            WorkerId = workerId,
            Capabilities = capabilities,
            MaxConcurrent = maxConcurrent
        };
    }

    // ---- Registration tests ----

    [Fact]
    public void Register_ThenGetAllWorkers_ContainsWorker()
    {
        var conn = CreateConnection();
        var info = MakeWorkerInfo("wkr_001", ["file.read", "file.list"]);

        var result = _registry.Register(info, conn);

        result.Should().BeTrue();
        var all = _registry.GetAllWorkers();
        all.Should().ContainSingle(w => w.WorkerId == "wkr_001");
    }

    [Fact]
    public void Register_SetsStateToReady()
    {
        var conn = CreateConnection();
        var info = MakeWorkerInfo("wkr_ready", ["file.read"]);

        _registry.Register(info, conn);

        var all = _registry.GetAllWorkers();
        var worker = all.First(w => w.WorkerId == "wkr_ready");
        worker.State.Should().Be(WorkerState.Ready);
    }

    [Fact]
    public void Register_MultipleWorkers_AllPresent()
    {
        _registry.Register(MakeWorkerInfo("wkr_a", ["cap.x"]), CreateConnection());
        _registry.Register(MakeWorkerInfo("wkr_b", ["cap.x"]), CreateConnection());
        _registry.Register(MakeWorkerInfo("wkr_c", ["cap.y"]), CreateConnection());

        _registry.GetAllWorkers().Should().HaveCount(3);
    }

    [Fact]
    public void Register_EmptyWorkerId_ReturnsFalse()
    {
        var conn = CreateConnection();
        var info = MakeWorkerInfo("", ["cap.x"]);

        var result = _registry.Register(info, conn);

        result.Should().BeFalse();
        _registry.GetAllWorkers().Should().BeEmpty();
    }

    [Fact]
    public void Register_ReRegisterSameId_ReplacesOldEntry()
    {
        var conn1 = CreateConnection();
        var conn2 = CreateConnection();
        var info1 = MakeWorkerInfo("wkr_dup", ["cap.a"]);
        var info2 = MakeWorkerInfo("wkr_dup", ["cap.b"]);

        _registry.Register(info1, conn1);
        _registry.Register(info2, conn2);

        _registry.GetAllWorkers().Should().ContainSingle(w => w.WorkerId == "wkr_dup");
        _registry.GetWorkersByCapability("cap.b").Should().ContainSingle();
        _registry.GetWorkersByCapability("cap.a").Should().BeEmpty();
    }

    // ---- Deregistration tests ----

    [Fact]
    public void Deregister_RemovesWorker()
    {
        _registry.Register(MakeWorkerInfo("wkr_del", ["cap.x"]), CreateConnection());
        _registry.GetAllWorkers().Should().HaveCount(1);

        var result = _registry.Deregister("wkr_del");

        result.Should().BeTrue();
        _registry.GetAllWorkers().Should().BeEmpty();
    }

    [Fact]
    public void Deregister_NonExistent_ReturnsFalse()
    {
        var result = _registry.Deregister("wkr_ghost");
        result.Should().BeFalse();
    }

    [Fact]
    public void Deregister_ClearsCapabilityIndex()
    {
        _registry.Register(MakeWorkerInfo("wkr_cap", ["cap.z"]), CreateConnection());
        _registry.HasAvailableWorker("cap.z").Should().BeTrue();

        _registry.Deregister("wkr_cap");

        _registry.HasAvailableWorker("cap.z").Should().BeFalse();
        _registry.GetWorkersByCapability("cap.z").Should().BeEmpty();
    }

    // ---- GetAvailableWorker round-robin tests ----

    [Fact]
    public void GetAvailableWorker_NoWorkers_ReturnsNull()
    {
        var result = _registry.GetAvailableWorker("cap.nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public void GetAvailableWorker_SingleWorker_ReturnsThatWorker()
    {
        var conn = CreateConnection();
        conn.WorkerId = "wkr_single";
        _registry.Register(MakeWorkerInfo("wkr_single", ["cap.s"]), conn);

        var result = _registry.GetAvailableWorker("cap.s");

        result.Should().NotBeNull();
        result!.WorkerId.Should().Be("wkr_single");
    }

    [Fact]
    public void GetAvailableWorker_RoundRobin_DistributesAcrossWorkers()
    {
        // Register two workers for the same capability
        var conn1 = CreateConnection();
        conn1.WorkerId = "wkr_rr1";
        var conn2 = CreateConnection();
        conn2.WorkerId = "wkr_rr2";

        _registry.Register(MakeWorkerInfo("wkr_rr1", ["cap.rr"]), conn1);
        _registry.Register(MakeWorkerInfo("wkr_rr2", ["cap.rr"]), conn2);

        // Call multiple times and verify both workers get selected
        var selectedIds = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            var result = _registry.GetAvailableWorker("cap.rr");
            result.Should().NotBeNull();
            selectedIds.Add(result!.WorkerId);
        }

        selectedIds.Should().Contain("wkr_rr1");
        selectedIds.Should().Contain("wkr_rr2");
    }

    [Fact]
    public void GetAvailableWorker_SkipsBusyWorker()
    {
        var conn1 = CreateConnection();
        conn1.WorkerId = "wkr_busy";
        var conn2 = CreateConnection();
        conn2.WorkerId = "wkr_ready";

        _registry.Register(MakeWorkerInfo("wkr_busy", ["cap.skip"], maxConcurrent: 1), conn1);
        _registry.Register(MakeWorkerInfo("wkr_ready", ["cap.skip"], maxConcurrent: 4), conn2);

        // Make the first worker busy
        _registry.IncrementActiveTask("wkr_busy");

        // Now all selections should go to wkr_ready since wkr_busy is at capacity
        for (int i = 0; i < 5; i++)
        {
            var result = _registry.GetAvailableWorker("cap.skip");
            result.Should().NotBeNull();
            result!.WorkerId.Should().Be("wkr_ready");
        }
    }

    // ---- IncrementActiveTask / DecrementActiveTask tests ----

    [Fact]
    public void IncrementActiveTask_IncreasesCount()
    {
        _registry.Register(MakeWorkerInfo("wkr_inc", ["cap.t"], maxConcurrent: 4), CreateConnection());

        _registry.IncrementActiveTask("wkr_inc");

        var worker = _registry.GetAllWorkers().First(w => w.WorkerId == "wkr_inc");
        worker.ActiveTasks.Should().Be(1);
    }

    [Fact]
    public void IncrementActiveTask_AtMaxConcurrent_SetsBusy()
    {
        _registry.Register(MakeWorkerInfo("wkr_busy2", ["cap.t"], maxConcurrent: 2), CreateConnection());

        _registry.IncrementActiveTask("wkr_busy2");
        _registry.IncrementActiveTask("wkr_busy2");

        var worker = _registry.GetAllWorkers().First(w => w.WorkerId == "wkr_busy2");
        worker.State.Should().Be(WorkerState.Busy);
        worker.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void DecrementActiveTask_DecreasesCount()
    {
        _registry.Register(MakeWorkerInfo("wkr_dec", ["cap.t"], maxConcurrent: 4), CreateConnection());
        _registry.IncrementActiveTask("wkr_dec");
        _registry.IncrementActiveTask("wkr_dec");

        _registry.DecrementActiveTask("wkr_dec");

        var worker = _registry.GetAllWorkers().First(w => w.WorkerId == "wkr_dec");
        worker.ActiveTasks.Should().Be(1);
    }

    [Fact]
    public void DecrementActiveTask_FromBusy_RestoresToReady()
    {
        _registry.Register(MakeWorkerInfo("wkr_restore", ["cap.t"], maxConcurrent: 1), CreateConnection());
        _registry.IncrementActiveTask("wkr_restore"); // now Busy

        _registry.DecrementActiveTask("wkr_restore");

        var worker = _registry.GetAllWorkers().First(w => w.WorkerId == "wkr_restore");
        worker.State.Should().Be(WorkerState.Ready);
        worker.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void DecrementActiveTask_BelowZero_ClampsToZero()
    {
        _registry.Register(MakeWorkerInfo("wkr_clamp", ["cap.t"]), CreateConnection());

        _registry.DecrementActiveTask("wkr_clamp");

        var worker = _registry.GetAllWorkers().First(w => w.WorkerId == "wkr_clamp");
        worker.ActiveTasks.Should().Be(0);
    }

    // ---- Concurrency safety test ----

    [Fact]
    public async Task GetAvailableWorker_100ConcurrentCalls_NoExceptions()
    {
        // Register a few workers
        for (int i = 0; i < 3; i++)
        {
            var conn = CreateConnection();
            conn.WorkerId = $"wkr_conc_{i}";
            _registry.Register(
                MakeWorkerInfo($"wkr_conc_{i}", ["cap.concurrent"], maxConcurrent: 100),
                conn);
        }

        // Fire 100 concurrent GetAvailableWorker calls
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => _registry.GetAvailableWorker("cap.concurrent")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All should return a connection (no exceptions, no nulls)
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public async Task ConcurrentRegisterAndDeregister_NoExceptions()
    {
        // Stress test: concurrent register and deregister operations
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var conn = CreateConnection();
                    var info = MakeWorkerInfo($"wkr_stress_{idx}", ["cap.stress"]);
                    _registry.Register(info, conn);
                    _registry.GetAvailableWorker("cap.stress");
                    _registry.Deregister($"wkr_stress_{idx}");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);

        exceptions.Should().BeEmpty();
    }

    // ---- Additional coverage ----

    [Fact]
    public void GetWorkersByCapability_ReturnsOnlyMatchingWorkers()
    {
        _registry.Register(MakeWorkerInfo("wkr_f1", ["file.read"]), CreateConnection());
        _registry.Register(MakeWorkerInfo("wkr_f2", ["file.read", "file.write"]), CreateConnection());
        _registry.Register(MakeWorkerInfo("wkr_f3", ["file.write"]), CreateConnection());

        var readWorkers = _registry.GetWorkersByCapability("file.read");
        readWorkers.Should().HaveCount(2);
        readWorkers.Select(w => w.WorkerId).Should().BeEquivalentTo(["wkr_f1", "wkr_f2"]);
    }

    [Fact]
    public void GetAvailableCount_ReturnsCorrectCount()
    {
        _registry.Register(MakeWorkerInfo("wkr_ac1", ["cap.count"], maxConcurrent: 1), CreateConnection());
        _registry.Register(MakeWorkerInfo("wkr_ac2", ["cap.count"], maxConcurrent: 1), CreateConnection());

        _registry.GetAvailableCount("cap.count").Should().Be(2);

        _registry.IncrementActiveTask("wkr_ac1"); // now busy

        _registry.GetAvailableCount("cap.count").Should().Be(1);
    }

    [Fact]
    public void UpdateHeartbeat_UpdatesTimestamp()
    {
        _registry.Register(MakeWorkerInfo("wkr_hb", ["cap.hb"]), CreateConnection());
        var before = _registry.GetAllWorkers().First().LastHeartbeat;

        // Small delay to ensure time difference
        Thread.Sleep(10);
        _registry.UpdateHeartbeat("wkr_hb");

        var after = _registry.GetAllWorkers().First().LastHeartbeat;
        after.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void SetWorkerState_ChangesState()
    {
        _registry.Register(MakeWorkerInfo("wkr_state", ["cap.st"]), CreateConnection());

        _registry.SetWorkerState("wkr_state", WorkerState.Draining);

        var worker = _registry.GetAllWorkers().First();
        worker.State.Should().Be(WorkerState.Draining);
        worker.IsAvailable.Should().BeFalse();
    }
}
