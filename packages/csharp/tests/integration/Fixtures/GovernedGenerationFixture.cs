using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Container;
using FunctionPool.Models;
using FunctionPool.Network;
using FunctionPool.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Broker host in Governed generation mode (HighLevelCoordinator:Generation:SystemScaffoldMode = Governed).
/// It has no container runtime, function pool or generation worker of its own, so it registers:
///   - <see cref="RecordingContainerManager"/>: records every agent spawn (including the registration secret
///     handed over as a secret environment value) and every stop, and can refuse spawns;
///   - <see cref="FakeGenerationWorkerRegistry"/>: reports the generation capabilities as served, or not;
///   - <see cref="FakeGenerationDispatcher"/>: plays the generation worker — it writes a small deterministic zip
///     into Generation:OutputRoot under the slot from the grant scope and returns the worker payload.
/// The broker's own GenerationIngestingDispatcher still wraps the dispatcher, so the ingest checks run for real.
/// </summary>
public sealed class GovernedGenerationFixture : BrokerAuthorizationFixture, IAsyncLifetime
{
    private readonly string _generationRoot;

    public GovernedGenerationFixture()
        : this(Path.Combine(Path.GetTempPath(), $"b4a-gengov-it-{Guid.NewGuid():N}"),
            new RecordingContainerManager(),
            new FakeGenerationWorkerRegistry())
    {
    }

    private GovernedGenerationFixture(string generationRoot, RecordingContainerManager containers, FakeGenerationWorkerRegistry registry)
        : base(
            enforceWorkerAuth: true,
            configureTestServices: services =>
            {
                services.RemoveAll<IContainerManager>();
                services.AddSingleton<IContainerManager>(containers);
                services.AddSingleton<IWorkerRegistry>(registry);
                services.RemoveAll<IExecutionDispatcher>();
                services.AddSingleton<IExecutionDispatcher>(new FakeGenerationDispatcher(Path.Combine(generationRoot, "out")));
            },
            hostSettings: new Dictionary<string, string?>
            {
                ["HighLevelCoordinator:Generation:SystemScaffoldMode"] = "Governed",
                ["Generation:OutputRoot"] = Path.Combine(generationRoot, "out"),
                ["Generation:WatchdogIntervalSeconds"] = "300",
                ["LlmProxy:Enabled"] = "true",
                // 確認 draft 時不向本機模型詢問執行模型建議
                ["HighLevelExecutionModelPolicy:Enabled"] = "false",
                ["FunctionPool:ContainerManager:AgentBrokerUrl"] = "http://host.containers.internal:5361",
            })
    {
        _generationRoot = generationRoot;
        Directory.CreateDirectory(OutputRoot);
        Containers = containers;
        Registry = registry;
    }

    public string OutputRoot => Path.Combine(_generationRoot, "out");
    public RecordingContainerManager Containers { get; }
    public FakeGenerationWorkerRegistry Registry { get; }

    public FakeGenerationDispatcher Dispatcher
        => (FakeGenerationDispatcher)Services.GetRequiredService<IExecutionDispatcher>();

    // IAsyncLifetime is re-implemented so xUnit calls this override, which also removes the output root.
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(_generationRoot, recursive: true); } catch { }
    }
}

public sealed class RecordingContainerManager : IContainerManager
{
    private int _counter;

    public List<ContainerSpawnRequest> Spawned { get; } = new();
    public List<string> Stopped { get; } = new();
    public bool RuntimeAvailable { get; set; } = true;
    public bool RefuseSpawns { get; set; }

    public Task<string> SpawnWorkerAsync(ContainerSpawnRequest request, CancellationToken ct = default)
    {
        if (RefuseSpawns)
            throw new InvalidOperationException("Max containers for 'agent' reached (3)");

        lock (Spawned)
        {
            Spawned.Add(request);
            return Task.FromResult($"c0ffee{Interlocked.Increment(ref _counter):D6}");
        }
    }

    public Task StopWorkerAsync(string containerId, CancellationToken ct = default)
    {
        lock (Stopped)
            Stopped.Add(containerId);
        return Task.CompletedTask;
    }

    public Task<List<ManagedContainer>> ListManagedAsync(CancellationToken ct = default)
        => Task.FromResult(new List<ManagedContainer>());

    public Task<string> GetLogsAsync(string containerId, int tailLines = 50, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(RuntimeAvailable);

    public Task<List<ContainerStats>> GetStatsAsync(CancellationToken ct = default)
        => Task.FromResult(new List<ContainerStats>());
}

/// <summary>Reports the three generation capabilities as served by one worker while <see cref="Registered"/> is true.</summary>
public sealed class FakeGenerationWorkerRegistry : IWorkerRegistry
{
    public bool Registered { get; set; } = true;

    private List<WorkerInfo> Workers() => Registered
        ? new List<WorkerInfo>
        {
            new()
            {
                WorkerId = "gen-wkr-test",
                Capabilities = new List<string> { "generation.catalog.query", "generation.definition.validate", "generation.scaffold.generate" },
                MaxConcurrent = 1,
                State = WorkerState.Ready
            }
        }
        : new List<WorkerInfo>();

    public bool Register(WorkerInfo worker, WorkerConnection connection) => false;
    public bool Deregister(string workerId) => false;
    public WorkerConnection? GetAvailableWorker(string capabilityId) => null;
    public List<WorkerInfo> GetWorkersByCapability(string capabilityId) => Workers().Where(w => w.Capabilities.Contains(capabilityId)).ToList();
    public List<WorkerInfo> GetAllWorkers() => Workers();
    public int GetAvailableCount(string capabilityId) => GetWorkersByCapability(capabilityId).Count;
    public bool HasAvailableWorker(string capabilityId) => GetAvailableCount(capabilityId) > 0;
    public void UpdateHeartbeat(string workerId) { }
    public void IncrementActiveTask(string workerId) { }
    public void DecrementActiveTask(string workerId) { }
    public void SetWorkerState(string workerId, WorkerState state) { }
}

/// <summary>
/// Stands in for the generation worker. catalog and validate answer like the generator CLI; generate writes
/// <c>{OutputRoot}/{output_slot}/{requestId}/{package_name}-scaffold.zip</c> (output location only from the grant
/// scope, as the real worker does) and returns the worker payload with a relative zip path.
/// </summary>
public sealed class FakeGenerationDispatcher : IExecutionDispatcher
{
    private readonly string _outputRoot;

    public FakeGenerationDispatcher(string outputRoot)
    {
        _outputRoot = outputRoot;
    }

    public List<ApprovedRequest> Dispatched { get; } = new();

    public Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
    {
        lock (Dispatched)
            Dispatched.Add(approvedRequest);

        return Task.FromResult(approvedRequest.Route switch
        {
            "query_component_catalog" => ExecutionResult.Ok(approvedRequest.RequestId, JsonSerializer.Serialize(new
            {
                ok = true,
                section = "overview",
                content = "DefinitionTemplate rules",
                catalog_sha256 = new string('a', 64),
                matrix_sha256 = new string('b', 64),
                summary_version = "1"
            })),
            "validate_definition" => ExecutionResult.Ok(approvedRequest.RequestId, JsonSerializer.Serialize(new
            {
                ok = true,
                errors = Array.Empty<object>(),
                warnings = Array.Empty<object>(),
                pages = new[] { new { id = "contacts-list", type = "list", field_count = 3 } },
                validation_digest = new string('d', 64),
                validator_version = "definition-validator/1.0.0"
            })),
            "generate_scaffold" => Generate(approvedRequest),
            _ => ExecutionResult.Fail(approvedRequest.RequestId, $"No fake handler for route '{approvedRequest.Route}'.")
        });
    }

    private ExecutionResult Generate(ApprovedRequest request)
    {
        var scope = JsonNode.Parse(request.Scope)!.AsObject();
        var slot = scope["output_slot"]!.GetValue<string>();
        var packageName = scope["package_name"]!.GetValue<string>();
        var directory = Path.Combine(_outputRoot, slot, request.RequestId);
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, $"{packageName}-scaffold.zip");

        using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in new[]
                     {
                         ("report/manifest.json", "{\"format\":\"definition-site-v1\"}"),
                         ("site/index.html", "<!doctype html><title>prototype</title>"),
                     })
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        var bytes = File.ReadAllBytes(zipPath);
        var payload = JsonSerializer.Serialize(new
        {
            output_slot = slot,
            request_id = request.RequestId,
            zip = new
            {
                path = $"{slot}/{request.RequestId}/{packageName}-scaffold.zip",
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                size = bytes.LongLength
            },
            pages = new[]
            {
                new { id = "contacts-list", type = "list", field_count = 3 },
                new { id = "contact-detail", type = "detail", field_count = 3 },
                new { id = "contact-form", type = "form", field_count = 3 },
            },
            file_count = 2,
            validation_digest = new string('d', 64),
            generator_version = "definition-site/1.0.0",
            catalog_sha256 = new string('a', 64)
        });

        var result = ExecutionResult.Ok(request.RequestId, payload);
        result.AnsweredByWorker = true;
        return result;
    }
}
