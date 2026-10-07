using System.Text.Json;
using Broker.Adapters;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Dispatch;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Unit.Tests.Helpers;

namespace Unit.Tests.Broker;

/// <summary>
/// 受治理生成的三個能力以 tool-spec 為唯一來源：broker 啟動時由 tool-spec 同步成 capability，
/// 不在 BrokerDbInitializer 的種子中重複定義；param schema 只用 SchemaValidator 支援的關鍵字；
/// 三個 route 都不在 InProcessDispatcher 的降級清單中（沒有 generation-worker 時就失敗，不會在 broker 程序內執行）。
/// </summary>
public sealed class GenerationToolSpecTests : IDisposable
{
    public static readonly (string ToolId, string CapabilityId, string Route, RiskLevel Risk, string Approval, ActionType Action, string Resource, int Quota)[] Expected =
    {
        ("generation.catalog.query", "generation.catalog.query", "query_component_catalog", RiskLevel.Low, "auto", ActionType.Read, "component_catalog", 20),
        ("generation.definition.validate", "generation.definition.validate", "validate_definition", RiskLevel.Low, "auto", ActionType.Read, "definition", 6),
        ("generation.scaffold.generate", "generation.scaffold.generate", "generate_scaffold", RiskLevel.Medium, "auto_if_task_scope_match", ActionType.Write, "generated_artifact", 2),
    };

    private static readonly HashSet<string> SupportedSchemaKeywords = new(StringComparer.Ordinal)
    {
        "type", "required", "properties", "items", "maxLength", "enum",
    };

    private readonly BrokerDb _db;
    private readonly string _dbPath;

    public GenerationToolSpecTests()
    {
        (_db, _dbPath) = TestDb.CreateIn(Path.Combine(Path.GetTempPath(), $"b4a-gen-toolspec-{Guid.NewGuid():N}"));
    }

    public void Dispose()
    {
        _db.Dispose();
        TestDb.Delete(_dbPath);
        try { Directory.Delete(Path.GetDirectoryName(_dbPath)!, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string BrokerProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "packages", "csharp", "broker");
            if (File.Exists(Path.Combine(candidate, "Broker.csproj")))
                return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Broker project directory not found above the test output.");
    }

    private ToolSpecRegistry LoadRepositoryRegistry()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(BrokerProjectDirectory());
        return new ToolSpecRegistry(environment, new ToolSpecRegistryOptions(), _db, NullLogger<ToolSpecRegistry>.Instance);
    }

    [Fact]
    public void Seed_DoesNotDefineTheGenerationCapabilities()
    {
        foreach (var expected in Expected)
            _db.Get<Capability>(expected.CapabilityId).Should().BeNull($"{expected.CapabilityId} comes only from its tool-spec");
    }

    [Fact]
    public async Task Sync_LoadsTheGenerationCapabilitiesFromTheirToolSpecs()
    {
        var registry = LoadRepositoryRegistry();
        await new ToolSpecCapabilitySyncService(registry, _db, NullLogger<ToolSpecCapabilitySyncService>.Instance)
            .StartAsync(CancellationToken.None);

        foreach (var expected in Expected)
        {
            var spec = registry.GetDefinitions().Single(definition => definition.ToolId == expected.ToolId);
            spec.Status.Should().Be("beta");
            spec.CapabilityBindings.Should().ContainSingle();
            spec.ExecutionRules.GetProperty("runtime_required").GetString().Should().Be("generation-worker");

            var capability = _db.Get<Capability>(expected.CapabilityId);
            capability.Should().NotBeNull(expected.CapabilityId);
            capability!.Route.Should().Be(expected.Route);
            capability.RiskLevel.Should().Be(expected.Risk);
            capability.ApprovalPolicy.Should().Be(expected.Approval);
            capability.ActionType.Should().Be(expected.Action);
            capability.ResourceType.Should().Be(expected.Resource);
            using var quota = JsonDocument.Parse(capability.Quota);
            quota.RootElement.GetProperty("max_calls").GetInt32().Should().Be(expected.Quota);

            using var schema = JsonDocument.Parse(capability.ParamSchema);
            AssertOnlySupportedKeywords(schema.RootElement, expected.CapabilityId);
        }
    }

    [Fact]
    public async Task Sync_ParamSchemas_AcceptTheAgentArguments_AndRejectWrongShapes()
    {
        var registry = LoadRepositoryRegistry();
        await new ToolSpecCapabilitySyncService(registry, _db, NullLogger<ToolSpecCapabilitySyncService>.Instance)
            .StartAsync(CancellationToken.None);
        var validator = new SchemaValidator();
        string Schema(string id) => _db.Get<Capability>(id)!.ParamSchema;

        validator.Validate("""{"section":"component","name":"DatePicker"}""", Schema("generation.catalog.query")).IsValid.Should().BeTrue();
        validator.Validate("""{}""", Schema("generation.catalog.query")).IsValid.Should().BeTrue();
        validator.Validate("""{"section":"all"}""", Schema("generation.catalog.query")).IsValid.Should().BeFalse();
        validator.Validate($$"""{"name":"{{new string('n', 65)}}"}""", Schema("generation.catalog.query")).IsValid.Should().BeFalse();

        validator.Validate("""{"template":{"kind":"definition-template"},"page_ids":["ContactListPage"]}""", Schema("generation.definition.validate")).IsValid.Should().BeTrue();
        validator.Validate("""{"page_ids":["ContactListPage"]}""", Schema("generation.definition.validate")).IsValid.Should().BeFalse();
        validator.Validate("""{"template":"text"}""", Schema("generation.definition.validate")).IsValid.Should().BeFalse();
        validator.Validate("""{"template":{},"page_ids":[1]}""", Schema("generation.definition.validate")).IsValid.Should().BeFalse();

        validator.Validate("""{"template":{},"title":"Contacts"}""", Schema("generation.scaffold.generate")).IsValid.Should().BeTrue();
        validator.Validate($$"""{"template":{},"title":"{{new string('t', 121)}}"}""", Schema("generation.scaffold.generate")).IsValid.Should().BeFalse();
        validator.Validate("""{"title":"Contacts"}""", Schema("generation.scaffold.generate")).IsValid.Should().BeFalse();
    }

    /// <summary>
    /// catalog 與 validate 是低風險、beta（視為啟用）的能力，Legacy 模式下也會同步進能力表；
    /// 但生成授予只由受治理生成的啟動流程寫入，所以 /agents/create 與 dashboard 的預設能力集合都不含 generation.*。
    /// </summary>
    [Fact]
    public async Task DefaultAgentCapabilitySets_LeaveOutTheGenerationCapabilities()
    {
        await new ToolSpecCapabilitySyncService(LoadRepositoryRegistry(), _db, NullLogger<ToolSpecCapabilitySyncService>.Instance)
            .StartAsync(CancellationToken.None);
        _db.Get<Capability>("generation.catalog.query")!.RiskLevel.Should().Be(RiskLevel.Low, "the regression needs a low-risk generation capability in the table");

        var spawn = new AgentSpawnService(_db);
        spawn.GetDefaultCapabilities().Should().NotBeEmpty()
            .And.NotContain(id => id.StartsWith("generation.", StringComparison.Ordinal));
        foreach (var taskType in new[] { "analysis", "rag", "assistant", "full", "other" })
        {
            spawn.GetCapabilitiesForTaskType(taskType).Should().NotBeEmpty(taskType)
                .And.NotContain(id => id.StartsWith("generation.", StringComparison.Ordinal), taskType);
        }

        var defaultAgent = spawn.CreateAgent(new AgentSpawnRequest { AgentId = "defaults", RequestedBy = "test" });
        defaultAgent.Success.Should().BeTrue(defaultAgent.Error);
        defaultAgent.GrantedCapabilities.Should().NotContain(id => id.StartsWith("generation.", StringComparison.Ordinal));
        defaultAgent.RuntimeDescriptor.Should().NotContain("generation.");

        var analysisAgent = spawn.CreateAgent(new AgentSpawnRequest { AgentId = "analysis", TaskType = "analysis", RequestedBy = "test" });
        analysisAgent.GrantedCapabilities.Should().NotContain(id => id.StartsWith("generation.", StringComparison.Ordinal));

        // 管理員明確選取時仍可授予（預設集合之外的選擇不受影響）。
        var chosen = spawn.CreateAgent(new AgentSpawnRequest
        {
            AgentId = "chosen",
            RequestedBy = "test",
            CapabilityIds = new List<string> { "generation.catalog.query" }
        });
        chosen.GrantedCapabilities.Should().Equal("generation.catalog.query");
    }

    [Fact]
    public void InProcessDispatcher_DoesNotHandleTheGenerationRoutes()
    {
        var dispatcher = new InProcessDispatcher(NullLogger<InProcessDispatcher>.Instance, Path.GetTempPath());

        foreach (var expected in Expected)
            dispatcher.CanHandle(expected.Route).Should().BeFalse($"{expected.Route} runs only on the generation worker");
    }

    [Fact]
    public async Task FallbackDispatcher_WithoutAGenerationWorker_FailsInsteadOfRunningInProcess()
    {
        var inProcess = new InProcessDispatcher(NullLogger<InProcessDispatcher>.Instance, Path.GetTempPath());
        var registry = new WorkerRegistry(NullLogger<WorkerRegistry>.Instance);
        var pool = new PoolDispatcher(registry, new PoolConfig(), NullLogger<PoolDispatcher>.Instance);
        var dispatcher = new FallbackDispatcher(pool, inProcess, inProcess.CanHandle, NullLogger<FallbackDispatcher>.Instance);

        foreach (var expected in Expected)
        {
            var result = await dispatcher.DispatchAsync(new ApprovedRequest
            {
                RequestId = $"req_{expected.Route}",
                CapabilityId = expected.CapabilityId,
                Route = expected.Route,
                Payload = JsonSerializer.Serialize(new { route = expected.Route, args = new { template = new { } } }),
                Scope = "{}",
                TaskId = "task_generation",
            });

            result.Success.Should().BeFalse(expected.Route);
            result.ErrorMessage.Should().Contain("No available worker", expected.Route);
        }
    }

    private static void AssertOnlySupportedKeywords(JsonElement schema, string capabilityId)
    {
        schema.ValueKind.Should().Be(JsonValueKind.Object);
        foreach (var property in schema.EnumerateObject())
        {
            SupportedSchemaKeywords.Should().Contain(property.Name, $"{capabilityId} param schema keyword");
            if (property.Name == "properties")
            {
                foreach (var child in property.Value.EnumerateObject())
                    AssertOnlySupportedKeywords(child.Value, capabilityId);
            }
            else if (property.Name == "items")
            {
                AssertOnlySupportedKeywords(property.Value, capabilityId);
            }
        }
    }
}
