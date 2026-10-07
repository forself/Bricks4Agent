using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;
using BrokerCore;
using BrokerCore.Crypto;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Container;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Broker;

/// <summary>
/// 受治理生成的啟動：任務指派主體、角色與三個 grant（scope 由 broker 寫入、配額以任務累計）、精簡的 runtime descriptor
/// （不帶受管路徑與使用者識別資料）、淨化過的工作項、AGENT_RUN 的大小上限、代理容器的啟動參數與註冊憑證、
/// 執行模型只在 LlmProxy 的供應者能服務時採用、名額上限，以及啟動失敗（含取消）與未就緒時的 fail-closed。
/// </summary>
public sealed class GovernedGenerationLauncherTests : IDisposable
{
    private readonly GovernedGenerationTestSupport _env = new();
    private readonly IContainerManager _containers = Substitute.For<IContainerManager>();
    private readonly List<ContainerSpawnRequest> _spawned = new();

    public GovernedGenerationLauncherTests()
    {
        _containers.SpawnWorkerAsync(Arg.Do<ContainerSpawnRequest>(request => _spawned.Add(request)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("c0ffee000010"));
    }

    public void Dispose() => _env.Dispose();

    private sealed class AlwaysReady : IGovernedGenerationReadiness
    {
        public Task<GovernedGenerationReadinessResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(GovernedGenerationReadinessResult.ReadyResult());
    }

    private GovernedGenerationLauncher Launcher(LlmProxyOptions? proxy = null, GovernedGenerationOptions? options = null)
    {
        var crypto = Substitute.For<IEnvelopeCrypto>();
        crypto.GetBrokerPublicKey().Returns("broker-public-key");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FunctionPool:ContainerManager:AgentBrokerUrl"] = "http://host.containers.internal:5361"
            })
            .Build();
        var spawnService = new AgentSpawnService(_env.Db, new RegistrationCredentialService(_env.Db), new SessionService(_env.Db));
        var containerLauncher = new AgentContainerLauncher(
            spawnService,
            _containers,
            crypto,
            configuration,
            new HighLevelLlmOptions { DefaultModel = "broker-default-model" },
            new RegistrationCredentialOptions());
        return new GovernedGenerationLauncher(
            options ?? new GovernedGenerationOptions { OutputRoot = _env.OutputRoot, AgentMaxIterations = 12, MaxPages = 12 },
            new AlwaysReady(),
            containerLauncher,
            spawnService,
            _env.Runs,
            _env.Db,
            NullLogger<GovernedGenerationLauncher>.Instance,
            llmProxyOptions: proxy ?? new LlmProxyOptions { Provider = "ollama", DefaultModel = "proxy-default-model" });
    }

    private BrokerTask SeedTask()
    {
        var task = new BrokerTask
        {
            TaskId = IdGen.New("task"),
            TaskType = "system_scaffold",
            SubmittedBy = $"line:{GovernedGenerationTestSupport.UserId}",
            RiskLevel = RiskLevel.Medium,
            State = TaskState.Created,
            ScopeDescriptor = "{}",
            RuntimeDescriptor = "{}",
            AssignedRoleId = GenerationCapabilities.ExecutorRole,
            CreatedAt = DateTime.UtcNow
        };
        _env.Db.Insert(task);
        return task;
    }

    private HighLevelTaskDraft Draft(string? summary = null) => new()
    {
        DraftId = IdGen.New("draft"),
        Channel = "line",
        UserId = GovernedGenerationTestSupport.UserId,
        TaskType = "system_scaffold",
        Title = "Generate system scaffold from line request",
        Summary = summary ?? "聯絡人管理系統：列表、明細與表單",
        ProjectName = "聯絡人 Demo",
        ProjectFolderName = "聯絡人 Demo",
        ScaffoldSpec = new HighLevelSystemScaffoldSpec
        {
            ScaffoldFamily = "admin_portal",
            FrontendStack = "vanilla_spa",
            UiComponentStrategy = "custom_component_library",
            BackendStack = "none",
            AuthMode = "none",
            RequestSummary = summary ?? "聯絡人管理系統：列表、明細與表單",
            ConfirmedRequirements = new List<string> { "project_scale=mini_app", "template_family=admin_portal" }
        }
    };

    // 與 coordinator 的 promoted descriptor 同形：帶受管路徑、使用者識別資料、hlm 文件 id 與推薦的執行模型。
    private string PromotedRuntimeDescriptor(string? provider = "ollama") => JsonSerializer.Serialize(new
    {
        source = "line",
        source_user_id = GovernedGenerationTestSupport.UserId,
        preferred_display_name = "Display Name",
        preferred_user_code = "usercode01",
        high_level = true,
        conversation_document = $"convlog:{GovernedGenerationTestSupport.UserId}",
        user_profile_document = $"hlm.profile.line.{GovernedGenerationTestSupport.UserId}",
        execution_intent_document = $"hlm.execution.line.{GovernedGenerationTestSupport.UserId}",
        requested_execution_model = new { alias = "execution-default", model = "planner-recommended-model", tier = "standard", provider },
        llm = new { default_model = "planner-recommended-model", allow_model_override = false },
        managed_paths = new { AccessRoot = _env.AccessRoot, ProjectRoot = Path.Combine(_env.AccessRoot, "line", "u", "projects", "demo") },
        project = new { required = true, name = "Demo", folder_name = "Demo" },
        scaffold = new { ScaffoldFamily = "admin_portal" }
    });

    [Theory]
    [InlineData("ContactsDemo", "ContactsDemo")]
    [InlineData("聯絡人 Demo!", "Demo")]
    [InlineData("聯絡人管理", "prototype")]
    [InlineData("my project v2", "my-project-v2")]
    [InlineData("CON", "site-CON")]
    [InlineData("../etc", "etc")]
    public void PackageName_IsReducedToTheWorkerCharacterSet(string folder, string expected)
    {
        GovernedGenerationLauncher.SanitizePackageName(folder).Should().Be(expected);
        GovernedGenerationLauncher.SanitizePackageName(new string('a', 200)).Should().HaveLength(60);
    }

    [Fact]
    public void GovernedScopeDescriptor_DropsHostPathsAndRequesterIdentity()
    {
        var promoted = JsonSerializer.Serialize(new
        {
            channel = "line",
            origin_user_id = "u1",
            mode = "production",
            execution_intent_id = "intent_1",
            execution_intent_document = "hlm.execution.line.u1",
            path_scope = new { access_root = _env.AccessRoot, paths = new[] { _env.AccessRoot } }
        });

        var governed = GovernedGenerationLauncher.BuildGovernedScopeDescriptor(promoted);

        governed.Should().NotContain("path_scope").And.NotContain(_env.AccessRoot.Replace("\\", "\\\\"));
        governed.Should().NotContain("origin_user_id").And.NotContain("u1").And.NotContain("execution_intent_document");
        var scope = JsonNode.Parse(governed)!;
        scope["channel"]!.GetValue<string>().Should().Be("line");
        scope["generation"]!.GetValue<string>().Should().Be("governed");
    }

    [Fact]
    public void Prepare_AssignsThePrincipalRoleAndTheThreeGrants_WithTheOutputSlotWrittenByTheBroker()
    {
        var task = SeedTask();
        var preparation = Launcher().Prepare(task, Draft(), PromotedRuntimeDescriptor(), _env.AccessRoot);

        var stored = _env.Db.Get<BrokerTask>(task.TaskId)!;
        stored.AssignedPrincipalId.Should().Be("prn_" + task.TaskId[5..]);
        stored.AssignedRoleId.Should().Be("role_executor");
        _env.Db.Get<Principal>(stored.AssignedPrincipalId!)!.ActorType.Should().Be(ActorType.AI);
        task.RuntimeDescriptor.Should().Be(stored.RuntimeDescriptor);

        var descriptor = TaskRuntimeDescriptor.Parse(stored.RuntimeDescriptor);
        descriptor.Llm.DefaultModel.Should().Be("planner-recommended-model");
        descriptor.CapabilityGrants.Select(grant => grant.CapabilityId).Should().Equal(
            "generation.catalog.query", "generation.definition.validate", "generation.scaffold.generate");
        descriptor.CapabilityGrants.Select(grant => grant.Quota).Should().Equal(20, 6, 2);
        descriptor.CapabilityGrants.Should().OnlyContain(grant => grant.IsTaskScopedQuota, "the quotas count per task, not per session");

        // 任務資料列會交給代理：只留 grant、生成上限與可用的 llm，不帶受管路徑、使用者識別資料或 hlm 文件 id。
        JsonNode.Parse(stored.RuntimeDescriptor)!.AsObject().Select(property => property.Key)
            .Should().BeEquivalentTo("capability_grants", "generation", "llm");
        stored.RuntimeDescriptor.Should().NotContain(_env.AccessRoot.Replace("\\", "\\\\"))
            .And.NotContain(GovernedGenerationTestSupport.UserId)
            .And.NotContain("hlm.");

        var generateScope = JsonNode.Parse(descriptor.CapabilityGrants[2].Scope.GetRawText())!.AsObject();
        generateScope["routes"]!.AsArray().Select(route => route!.GetValue<string>()).Should().Equal("generate_scaffold");
        generateScope["output_slot"]!.GetValue<string>().Should().Be(task.TaskId);
        generateScope["package_name"]!.GetValue<string>().Should().Be("Demo");
        generateScope["max_pages"]!.GetValue<int>().Should().Be(12);
        generateScope["package"]!.GetValue<string>().Should().Be("definition-site-v1");
        JsonNode.Parse(descriptor.CapabilityGrants[0].Scope.GetRawText())!.AsObject().Select(p => p.Key).Should().Equal("routes");
        var validateScope = JsonNode.Parse(descriptor.CapabilityGrants[1].Scope.GetRawText())!.AsObject();
        validateScope["routes"]!.AsArray().Select(route => route!.GetValue<string>()).Should().Equal("validate_definition");
        validateScope["max_pages"]!.GetValue<int>().Should().Be(12, "validate reports too many pages before generate");

        preparation.OutputSlot.Should().Be(task.TaskId);
        preparation.Request.Limits.MaxPages.Should().Be(12);
        preparation.Request.Scaffold["family"].Should().Be("admin_portal");
        // 舊 scaffold 的技術棧（前端框架、後端、資料庫、登入、部署）不適用於受治理生成的產物，不放進工作項
        preparation.Request.Scaffold.Keys.Should().BeSubsetOf(new[] { "family", "ui_shape" });
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData(null)]
    public async Task Prepare_IgnoresARecommendedModelTheLlmProxyProviderCannotServe(string? catalogProvider)
    {
        var launcher = Launcher();
        var task = SeedTask();
        var draft = Draft();

        var preparation = launcher.Prepare(task, draft, PromotedRuntimeDescriptor(catalogProvider), _env.AccessRoot);

        var stored = _env.Db.Get<BrokerTask>(task.TaskId)!;
        TaskRuntimeDescriptor.Parse(stored.RuntimeDescriptor).Llm.HasOverrides.Should().BeFalse();
        JsonNode.Parse(stored.RuntimeDescriptor)!.AsObject().ContainsKey("llm").Should().BeFalse();

        (await launcher.LaunchAsync(task, new Plan { PlanId = IdGen.New("plan"), TaskId = task.TaskId }, draft, preparation, CancellationToken.None))
            .Success.Should().BeTrue();
        _spawned.Single().TrustedEnvironment["AGENT_MODEL"].Should().Be("proxy-default-model",
            "without a servable recommendation the agent uses LlmProxy:DefaultModel");
    }

    [Theory]
    [InlineData("ollama", "ollama", true)]
    [InlineData("Ollama", "ollama", true)]
    [InlineData("claude", "anthropic", true)]
    [InlineData("ollama", "anthropic", false)]
    [InlineData("", "ollama", false)]
    [InlineData(null, "ollama", false)]
    public void ProviderMatching_IsCaseInsensitive_AndNeverMatchesAMissingProvider(string? catalog, string proxy, bool expected)
        => GovernedGenerationLauncher.IsSameProvider(catalog, proxy).Should().Be(expected);

    [Fact]
    public void Capacity_IsLimitedPerUser_AndInTotal()
    {
        var options = new GovernedGenerationOptions { OutputRoot = _env.OutputRoot, MaxConcurrentRuns = 2, MaxConcurrentRunsPerUser = 1 };
        var launcher = Launcher(options: options);
        launcher.CheckCapacity("line", GovernedGenerationTestSupport.UserId).Should().BeNull();

        _env.SeedRun();
        launcher.CheckCapacity("line", GovernedGenerationTestSupport.UserId).Should().Be(GovernedGenerationErrors.InProgress);
        launcher.CheckCapacity("line", "another-user").Should().BeNull();

        _env.SeedRun(status: GovernedGenerationRunStatus.Ingested);
        launcher.CheckCapacity("line", "another-user").Should().Be(GovernedGenerationErrors.Busy, "two runs are open");

        _env.SeedRun(status: GovernedGenerationRunStatus.Delivered);
        _env.SeedRun(status: GovernedGenerationRunStatus.Failed);
        options.MaxConcurrentRuns = 3;
        launcher.CheckCapacity("line", "another-user").Should().BeNull("finished runs do not count");
    }

    [Theory]
    [InlineData(12, 12)]
    [InlineData(20, 12)]
    [InlineData(100, 12)]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    public void MaxPages_IsLimitedToWhatTheGeneratorValidates(int configured, int resolved)
        => new GovernedGenerationOptions { MaxPages = configured }.ResolveMaxPages().Should().Be(resolved);

    [Fact]
    public void WorkItem_CarriesNoHostPath_AndAgentRunStaysUnderTheLimit()
    {
        var hostPath = Path.Combine(_env.AccessRoot, "line", GovernedGenerationTestSupport.UserId, "documents", "secret.txt");
        var longSummary = $"請參考 {hostPath} 的內容。" + string.Concat(Enumerable.Repeat("需要聯絡人列表、明細與表單，", 400));
        var draft = Draft(longSummary);
        draft.ScaffoldSpec!.ConfirmedRequirements.AddRange(Enumerable.Range(0, 40).Select(i => $"requirement_{i}=" + new string('x', 150)));

        var preparation = Launcher().Prepare(SeedTask(), draft, PromotedRuntimeDescriptor(), _env.AccessRoot);
        var requestJson = JsonSerializer.Serialize(preparation.Request);
        requestJson.Should().NotContain(_env.Root.Replace("\\", "\\\\")).And.NotContain(_env.Root);

        var run = GovernedGenerationLauncher.BuildAgentRun("task_0000000000AA_00000000000000BB", preparation.Request);
        Encoding.UTF8.GetByteCount(run).Should().BeLessThanOrEqualTo(GovernedGenerationLauncher.MaxAgentRunBytes);
        run.Should().Contain("task_0000000000AA_00000000000000BB").And.Contain("WORK_ITEM_JSON: ");
        var workItem = JsonNode.Parse(run[(run.IndexOf("WORK_ITEM_JSON: ", StringComparison.Ordinal) + "WORK_ITEM_JSON: ".Length)..])!.AsObject();
        workItem["kind"]!.GetValue<string>().Should().Be("system_scaffold_generation");
        workItem["limits"]!["max_pages"]!.GetValue<int>().Should().Be(12);
        run.Should().NotContain(_env.Root);

        // 與 system prompt 的工作流程一致：代理無人應答、每一回合都要呼叫工具，generate 只在流程允許時重試。
        var instructions = run[..run.IndexOf("WORK_ITEM_JSON: ", StringComparison.Ordinal)];
        instructions.Should().Contain("call a tool in every turn until the final summary");
        instructions.Should().Contain("call it again only when the workflow allows a retry");
        instructions.Should().NotContain("generate once");
    }

    [Fact]
    public async Task Launch_SpawnsTheAgentWithTheWorkItem_AndHandsTheCredentialOverAsASecret()
    {
        var launcher = Launcher();
        var task = SeedTask();
        var draft = Draft();
        var preparation = launcher.Prepare(task, draft, PromotedRuntimeDescriptor(), _env.AccessRoot);

        var result = await launcher.LaunchAsync(task, new Plan { PlanId = IdGen.New("plan"), TaskId = task.TaskId }, draft, preparation, CancellationToken.None);

        result.Success.Should().BeTrue();
        var request = _spawned.Should().ContainSingle().Subject;
        request.WorkerType.Should().Be("agent");
        request.WorkerId.Should().Be(GovernedGenerationLauncher.BuildAgentWorkerId(task.TaskId));
        request.TrustedEnvironment["BROKER_TASK_ID"].Should().Be(task.TaskId);
        request.TrustedEnvironment["BROKER_PRINCIPAL_ID"].Should().Be(preparation.PrincipalId);
        request.TrustedEnvironment["BROKER_ROLE_ID"].Should().Be("role_executor");
        request.TrustedEnvironment["BROKER_URL"].Should().Be("http://host.containers.internal:5361");
        request.TrustedEnvironment["AGENT_MAX_ITERATIONS"].Should().Be("12");
        request.TrustedEnvironment["AGENT_MODEL"].Should().Be("planner-recommended-model");
        request.TrustedEnvironment["AGENT_RUN"].Should().Contain("WORK_ITEM_JSON");
        Encoding.UTF8.GetByteCount(request.TrustedEnvironment["AGENT_RUN"]).Should().BeLessThan(4096);
        request.TrustedEnvironment.Keys.Should().NotContain(AgentContainerLauncher.RegistrationSecretEnvironmentVariable);
        var secret = request.SecretEnvironment[AgentContainerLauncher.RegistrationSecretEnvironmentVariable];
        new RegistrationCredentialService(_env.Db).Verify(preparation.PrincipalId, task.TaskId, secret).Succeeded.Should().BeTrue();

        var run = _env.Runs.Get(task.TaskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Running);
        run.ContainerId.Should().Be("c0ffee000010");
        run.OutputSlot.Should().Be(task.TaskId);
        run.PackageName.Should().Be("Demo");
        run.DeadlineAt.Should().BeAfter(run.CreatedAt);
    }

    [Fact]
    public async Task LaunchFailure_FailsTheTask_RevokesTheCredential_AndRecordsTheFailure()
    {
        _containers.SpawnWorkerAsync(Arg.Any<ContainerSpawnRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("Max containers for 'agent' reached (3)")));
        var launcher = Launcher();
        var task = SeedTask();
        var draft = Draft();
        var preparation = launcher.Prepare(task, draft, PromotedRuntimeDescriptor(), _env.AccessRoot);

        var result = await launcher.LaunchAsync(task, new Plan { PlanId = IdGen.New("plan"), TaskId = task.TaskId }, draft, preparation, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("generation_launch_failed");
        _env.Db.Get<BrokerTask>(task.TaskId)!.State.Should().Be(TaskState.Failed);
        _env.Db.Get<Principal>(preparation.PrincipalId)!.Status.Should().Be(EntityStatus.Disabled);
        _env.Db.Query<RegistrationCredential>("SELECT * FROM registration_credentials WHERE task_id = @taskId", new { taskId = task.TaskId })
            .Should().OnlyContain(credential => credential.RevokedAt != null);
        var run = _env.Runs.Get(task.TaskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        run.FailureReason.Should().Be("agent_launch_failed");
    }

    [Fact]
    public async Task LaunchCancelledByTheRequest_StillFailsTheTaskAndTheRun()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _containers.SpawnWorkerAsync(Arg.Any<ContainerSpawnRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new OperationCanceledException(cancelled.Token)));
        var launcher = Launcher();
        var task = SeedTask();
        var draft = Draft();
        var preparation = launcher.Prepare(task, draft, PromotedRuntimeDescriptor(), _env.AccessRoot);

        var result = await launcher.LaunchAsync(task, new Plan { PlanId = IdGen.New("plan"), TaskId = task.TaskId }, draft, preparation, cancelled.Token);

        result.Success.Should().BeFalse();
        _env.Db.Get<BrokerTask>(task.TaskId)!.State.Should().Be(TaskState.Failed, "a cancelled launch must not leave the task active");
        _env.Db.Get<Principal>(preparation.PrincipalId)!.Status.Should().Be(EntityStatus.Disabled);
        _env.Runs.Get(task.TaskId)!.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        _env.Runs.ListOpen().Should().NotContain(run => run.TaskId == task.TaskId);
    }

    // ── 就緒檢查 ──

    private sealed class Prerequisites
    {
        public bool ContainerManagerEnabled { get; set; } = true;
        public bool RuntimeAvailable { get; set; } = true;
        public bool LlmProxyEnabled { get; set; } = true;
        public bool CapabilitiesLoaded { get; set; } = true;
        public bool PoolEnabled { get; set; } = true;
        public bool WorkerRegistered { get; set; } = true;
        public bool OutputRootExists { get; set; } = true;
        public string Mode { get; set; } = SystemScaffoldModes.Governed;
    }

    private async Task<GovernedGenerationReadinessResult> CheckAsync(Prerequisites prerequisites)
    {
        IContainerManager containers = prerequisites.ContainerManagerEnabled ? Substitute.For<IContainerManager>() : new NoOpContainerManager();
        if (prerequisites.ContainerManagerEnabled)
            containers.IsRuntimeAvailableAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prerequisites.RuntimeAvailable));

        var llm = Substitute.For<ILlmProxyService>();
        llm.IsEnabled.Returns(prerequisites.LlmProxyEnabled);

        if (prerequisites.CapabilitiesLoaded)
        {
            foreach (var capabilityId in GenerationCapabilities.All)
            {
                if (_env.Db.Get<Capability>(capabilityId) == null)
                    _env.Db.Insert(new Capability { CapabilityId = capabilityId, Route = capabilityId, ResourceType = "generation" });
            }
        }

        var registries = new List<IWorkerRegistry>();
        if (prerequisites.PoolEnabled)
        {
            var registry = Substitute.For<IWorkerRegistry>();
            registry.GetWorkersByCapability(Arg.Any<string>()).Returns(_ => prerequisites.WorkerRegistered
                ? new List<WorkerInfo> { new() { WorkerId = "gen-wkr-1" } }
                : new List<WorkerInfo>());
            registries.Add(registry);
        }

        var outputRoot = prerequisites.OutputRootExists ? _env.OutputRoot : Path.Combine(_env.Root, "missing");
        var readiness = new GovernedGenerationReadiness(
            new HighLevelCoordinatorOptions { Generation = new HighLevelGenerationOptions { SystemScaffoldMode = prerequisites.Mode } },
            new GovernedGenerationOptions { OutputRoot = outputRoot },
            containers,
            llm,
            _env.Db,
            registries);
        return await readiness.CheckAsync();
    }

    [Fact]
    public async Task Readiness_AllPrerequisitesMet_IsReady()
    {
        var result = await CheckAsync(new Prerequisites());
        result.Ready.Should().BeTrue(string.Join(" ", result.Reasons));
    }

    public static TheoryData<string> MissingPrerequisites => new()
    {
        "container-manager", "runtime", "llm-proxy", "capabilities", "pool", "worker", "output-root", "mode"
    };

    [Theory]
    [MemberData(nameof(MissingPrerequisites))]
    public async Task Readiness_AnyMissingPrerequisite_IsNotReady(string missing)
    {
        var prerequisites = new Prerequisites();
        switch (missing)
        {
            case "container-manager": prerequisites.ContainerManagerEnabled = false; break;
            case "runtime": prerequisites.RuntimeAvailable = false; break;
            case "llm-proxy": prerequisites.LlmProxyEnabled = false; break;
            case "capabilities": prerequisites.CapabilitiesLoaded = false; break;
            case "pool": prerequisites.PoolEnabled = false; break;
            case "worker": prerequisites.WorkerRegistered = false; break;
            case "output-root": prerequisites.OutputRootExists = false; break;
            case "mode": prerequisites.Mode = "Governd"; break;
        }

        var result = await CheckAsync(prerequisites);

        result.Ready.Should().BeFalse();
        result.Reasons.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null, "Legacy", false)]
    [InlineData("", "Legacy", false)]
    [InlineData("legacy", "Legacy", false)]
    [InlineData("Governed", "Governed", true)]
    [InlineData("governed", "Governed", true)]
    [InlineData("Governd", "Invalid", true)]
    public void Mode_UnknownValuesTakeTheGovernedPath_SoTheyFailClosed(string? value, string resolved, bool governedPath)
    {
        var options = new HighLevelGenerationOptions { SystemScaffoldMode = value! };
        options.ResolveSystemScaffoldMode().Should().Be(resolved);
        options.UsesGovernedPath.Should().Be(governedPath);
    }
}
