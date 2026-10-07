using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;
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
///   - <see cref="ScriptedExecutionModelPlanner"/>: the execution model recommendation a test sets (none by default);
///   - <see cref="FakeGenerationDispatcher"/>: plays the generation worker — it writes a small deterministic zip
///     into Generation:OutputRoot under the slot from the grant scope and returns the worker payload; a test can
///     hand its requests to <see cref="InProcessGenerationWorker"/> instead (the worker's real handlers and the
///     repository's generator CLI, writing into the same output root).
/// The broker's own GenerationIngestingDispatcher still wraps the dispatcher, so the ingest checks run for real.
/// </summary>
public sealed class GovernedGenerationFixture : BrokerAuthorizationFixture, IAsyncLifetime
{
    private readonly string _generationRoot;

    public GovernedGenerationFixture()
        : this(Path.Combine(Path.GetTempPath(), $"b4a-gengov-it-{Guid.NewGuid():N}"),
            new RecordingContainerManager(),
            new FakeGenerationWorkerRegistry(),
            new ScriptedExecutionModelPlanner())
    {
    }

    private GovernedGenerationFixture(
        string generationRoot,
        RecordingContainerManager containers,
        FakeGenerationWorkerRegistry registry,
        ScriptedExecutionModelPlanner planner)
        : base(
            enforceWorkerAuth: true,
            configureTestServices: services =>
            {
                services.RemoveAll<IContainerManager>();
                services.AddSingleton<IContainerManager>(containers);
                services.AddSingleton<IWorkerRegistry>(registry);
                services.RemoveAll<IExecutionDispatcher>();
                services.AddSingleton<IExecutionDispatcher>(new FakeGenerationDispatcher(Path.Combine(generationRoot, "out")));
                services.RemoveAll<IHighLevelExecutionModelPlanner>();
                services.AddSingleton<IHighLevelExecutionModelPlanner>(planner);
            },
            hostSettings: new Dictionary<string, string?>
            {
                ["HighLevelCoordinator:Generation:SystemScaffoldMode"] = "Governed",
                ["Generation:OutputRoot"] = Path.Combine(generationRoot, "out"),
                ["Generation:WatchdogIntervalSeconds"] = "300",
                // 各測試類別共用同一個 host，先前測試留下的執行會占用名額；上限的測試自行調低。
                ["Generation:MaxConcurrentRuns"] = "50",
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
        Planner = planner;
    }

    public string OutputRoot => Path.Combine(_generationRoot, "out");
    public RecordingContainerManager Containers { get; }
    public FakeGenerationWorkerRegistry Registry { get; }
    public ScriptedExecutionModelPlanner Planner { get; }

    public FakeGenerationDispatcher Dispatcher
        => (FakeGenerationDispatcher)Services.GetRequiredService<IExecutionDispatcher>();

    // IAsyncLifetime is re-implemented so xUnit calls this override, which also removes the output root.
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(_generationRoot, recursive: true); } catch { }
    }
}

/// <summary>The execution model recommendation the coordinator receives on confirm (null: no recommendation).</summary>
public sealed class ScriptedExecutionModelPlanner : IHighLevelExecutionModelPlanner
{
    public HighLevelExecutionModelRequest? Recommendation { get; set; }

    public Task<HighLevelExecutionModelRequest?> RecommendAsync(
        HighLevelTaskDraft draft,
        HighLevelMemoryState memory,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Recommendation);
}

public sealed class RecordingContainerManager : IContainerManager
{
    private int _counter;
    private TaskCompletionSource _runtimeCheckStarted = NewSignal();

    public List<ContainerSpawnRequest> Spawned { get; } = new();
    public List<string> Stopped { get; } = new();
    public bool RuntimeAvailable { get; set; } = true;
    public bool RefuseSpawns { get; set; }

    /// <summary>
    /// When set, the runtime check of the governed readiness test waits for it, so a confirm can be held
    /// between the project name re-check and the task creation; <see cref="RuntimeCheckStarted"/> signals the first wait.
    /// </summary>
    public TaskCompletionSource? HoldRuntimeChecks { get; set; }

    public Task RuntimeCheckStarted => _runtimeCheckStarted.Task;

    public void ResetRuntimeCheckStarted() => _runtimeCheckStarted = NewSignal();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public async Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default)
    {
        var hold = HoldRuntimeChecks;
        if (hold != null)
        {
            _runtimeCheckStarted.TrySetResult();
            await hold.Task;
        }

        return RuntimeAvailable;
    }

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
/// When <see cref="Worker"/> is set, requests go to that dispatcher instead (for example
/// <see cref="InProcessGenerationWorker"/>, the real handlers with the repository's generator CLI).
/// </summary>
public sealed class FakeGenerationDispatcher : IExecutionDispatcher
{
    private readonly string _outputRoot;

    public FakeGenerationDispatcher(string outputRoot)
    {
        _outputRoot = outputRoot;
    }

    public List<ApprovedRequest> Dispatched { get; } = new();

    /// <summary>Dispatcher that answers instead of the fake (null: the fake answers).</summary>
    public IExecutionDispatcher? Worker { get; set; }

    public Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
    {
        lock (Dispatched)
            Dispatched.Add(approvedRequest);

        if (Worker != null)
            return Worker.DispatchAsync(approvedRequest);

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

/// <summary>
/// The generation worker inside the test process: the worker's real handlers
/// (catalog, validate, generate) with the repository's generator CLI (tools/generation/cli.mjs run by node),
/// writing into the same Generation:OutputRoot the broker reads. Requests are routed by capability id and the
/// handler result is mapped the way the function pool maps a WORKER_RESULT frame, so the broker sees exactly
/// what a registered generation-worker would answer; only the TCP frame transport is left out.
/// </summary>
public sealed class InProcessGenerationWorker : IExecutionDispatcher
{
    private readonly Dictionary<string, WorkerSdk.ICapabilityHandler> _handlers;

    public InProcessGenerationWorker(string outputRoot)
    {
        Options = new GenerationWorker.Support.GenerationWorkerOptions
        {
            NodePath = GenerationWorker.Support.GenerationWorkerOptions.ResolveNodePath(null),
            ToolsRoot = FindRepositoryRoot(),
            OutputRoot = outputRoot,
            QueryTimeout = TimeSpan.FromSeconds(60),
            BuildTimeout = TimeSpan.FromSeconds(120),
        };
        var configurationError = Options.Validate();
        if (configurationError != null)
            throw new InvalidOperationException(configurationError);

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var cli = new GenerationWorker.Support.NodeGeneratorCli(Options, logger);
        _handlers = new WorkerSdk.ICapabilityHandler[]
        {
            new GenerationWorker.Handlers.CatalogQueryHandler(Options, cli, logger),
            new GenerationWorker.Handlers.DefinitionValidateHandler(Options, cli, logger),
            new GenerationWorker.Handlers.ScaffoldGenerateHandler(Options, cli, logger),
        }.ToDictionary(handler => handler.CapabilityId, StringComparer.Ordinal);
    }

    public GenerationWorker.Support.GenerationWorkerOptions Options { get; }

    /// <summary>The repository root (the directory that contains tools/generation/cli.mjs).</summary>
    public static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "generation", "cli.mjs")))
                return directory.FullName;
        }

        throw new InvalidOperationException("tools/generation/cli.mjs not found above the test output.");
    }

    public async Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
    {
        if (!_handlers.TryGetValue(approvedRequest.CapabilityId, out var handler))
        {
            return Answered(ExecutionResult.Fail(approvedRequest.RequestId,
                $"No handler for capability '{approvedRequest.CapabilityId}'"));
        }

        var (success, resultPayload, error) = await handler.ExecuteAsync(
            approvedRequest.RequestId, approvedRequest.Route, approvedRequest.Payload, approvedRequest.Scope, CancellationToken.None);

        return Answered(success
            ? ExecutionResult.Ok(approvedRequest.RequestId, resultPayload ?? "{}")
            : ExecutionResult.Fail(approvedRequest.RequestId, error ?? "Worker execution failed"));
    }

    private static ExecutionResult Answered(ExecutionResult result)
    {
        result.AnsweredByWorker = true;
        return result;
    }
}
