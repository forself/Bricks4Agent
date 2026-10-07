using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Container;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Api;

/// <summary>
/// 受治理生成（Governed）的 broker 端整條路（設計 §10.3）：
/// - 使用者確認 system_scaffold draft 後，任務指派 AI 主體與 role_executor、帶三個 grant（generate 的 scope 由 broker 寫入），
///   broker 程序內沒有寫出任何檔案，代理容器以註冊憑證啟動，回覆「已受理」且不帶主機路徑；
/// - 以該憑證註冊的代理 session 送出 catalog、validate、generate：generate 經 broker 驗證收下後為 Succeeded 並帶 evidenceRef，
///   稽核鏈依序 RECEIVED → DISPATCHED → SUCCEEDED；交付後有產物紀錄（portal 產物清單）與 LINE 通知，任務 Completed、容器停止；
/// - 沒有生成 grant 的 session 送 generate 得到 Denied；
/// - 前置條件不滿足（沒有 generation-worker、沒有容器執行環境）時 fail-closed：不建立任務、不啟動代理、不退回程序內生成；
/// - 代理啟動失敗時任務標為 Failed，draft 保留，專案名稱可再用；
/// - 同一份 draft 並行確認只建立一個任務；同一使用者已有進行中的生成、或全部生成已達上限時不建立任務；
/// - 綁定任務的代理 session 經 /tasks/query、/runtime/spec、context 與 plans 讀不到受管根目錄或發起使用者的 id；
/// - 管理員能以 /agents/list 列出的 id 經 /agents/stop 停止受治理生成的代理；
/// - 代理重新註冊（容器重啟）時，生成能力的配額以任務累計；
/// - 執行模型的推薦只在供應者與 LlmProxy 相同時採用，否則代理用 LlmProxy:DefaultModel；
/// - 程序內端到端：同一條路改由 generation-worker 真正的 handler 與 repo 中的生成器 CLI 處理 golden 範例
///   （catalog → validate 失敗後修正 → generate），broker 驗證收下的 zip 與 manifest 一致並交付。
/// </summary>
public sealed class GovernedGenerationTests : IClassFixture<GovernedGenerationFixture>
{
    private const string LineProcessPath = "/api/v1/high-level/line/process";
    private const string SubmitPath = "/api/v1/execution-requests/submit";
    private const string LoopbackAddress = "127.0.0.1";

    private readonly GovernedGenerationFixture _fixture;
    private readonly EncryptedBrokerClient _client;

    public GovernedGenerationTests(GovernedGenerationFixture fixture)
    {
        _fixture = fixture;
        _client = new EncryptedBrokerClient(fixture.Client, fixture.Services);
        _fixture.Registry.Registered = true;
        _fixture.Containers.RuntimeAvailable = true;
        _fixture.Containers.RefuseSpawns = false;
        _fixture.Containers.HoldRuntimeChecks = null;
        _fixture.Dispatcher.Worker = null;
        _fixture.Planner.Recommendation = null;
    }

    private HighLevelCoordinator Coordinator => _fixture.Services.GetRequiredService<HighLevelCoordinator>();

    private async Task<JsonElement> SendLineAsync(string userId, string message)
    {
        var reply = await _fixture.SendLineWorkerSignedAsync(
            HttpMethod.Post,
            LineProcessPath,
            JsonSerializer.Serialize(new { user_id = userId, message }));
        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the line process response was {0}", reply);
        using var document = JsonDocument.Parse(reply.Body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<string> NewProductionUserAsync(string prefix)
    {
        var userId = $"{prefix}-{Guid.NewGuid():N}";
        await SendLineAsync(userId, "hello");
        Coordinator.ReviewLineUserRegistration(userId, "member");
        Coordinator.SetLineUserPermissions(userId, new HighLevelUserPermissionsPatch { AllowProduction = true });
        return userId;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsNullOrMissing(JsonElement element, string name)
        => !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private HighLevelManagedPaths Paths(string userId) => Coordinator.GetLineManagedPaths(userId)!;

    private List<BrokerTask> TasksSubmittedBy(string userId)
        => _fixture.Db.Query<BrokerTask>("SELECT * FROM broker_tasks WHERE submitted_by = @submittedBy", new { submittedBy = $"line:{userId}" });

    private async Task<BrokerReply> SubmitAsync(BrokerTestSession session, string capabilityId, string route, object args)
        => await _client.SendEncryptedAsync(session, SubmitPath, new
        {
            capability_id = capabilityId,
            idempotency_key = Guid.NewGuid().ToString("N"),
            intent = $"governed generation test: {route}",
            payload = new { route, args, project_root = "/workspace" }
        }, session.ScopedToken);

    private static object GoldenLikeTemplate() => new
    {
        kind = "definition-template",
        version = "0.1.0",
        definitions = new
        {
            pages = new[]
            {
                new { id = "contacts-list", definition = new { name = "ContactListPage", type = "list", fields = new[] { new { name = "fullName", type = "text", label = "Name" } } } }
            }
        }
    };

    [Fact]
    public async Task ConfirmedScaffold_LaunchesAGovernedAgent_ThenTheGeneratedPackageIsVerifiedAndDelivered()
    {
        var userId = await NewProductionUserAsync("line-gengov");
        var draft = await SendLineAsync(userId, "/建立 完整系統雛形 聯絡人管理 #ContactsDemo");
        IsNullOrMissing(draft, "error").Should().BeTrue();

        var confirmed = await SendLineAsync(userId, "y");

        // ── 回覆：已受理，不帶主機路徑 ──
        IsNullOrMissing(confirmed, "error").Should().BeTrue("the confirm reply was {0}", confirmed);
        var reply = ReadString(confirmed, "reply")!;
        reply.Should().Contain("已受理").And.Contain("project_folder: ContactsDemo");
        reply.Should().NotContain(_fixture.OutputRoot).And.NotContain(Path.GetTempPath().TrimEnd('\\', '/'));

        // ── 任務：AI 主體、role_executor、三個 grant；scope 不帶主機路徑 ──
        var task = TasksSubmittedBy(userId).Single();
        reply.Should().Contain(task.TaskId);
        task.TaskType.Should().Be("system_scaffold");
        task.AssignedPrincipalId.Should().Be("prn_" + task.TaskId[5..]);
        task.AssignedRoleId.Should().Be("role_executor");
        task.ScopeDescriptor.Should().NotContain("path_scope");
        _fixture.Db.Get<Principal>(task.AssignedPrincipalId!)!.ActorType.Should().Be(ActorType.AI);

        var descriptor = TaskRuntimeDescriptor.Parse(task.RuntimeDescriptor);
        descriptor.CapabilityGrants.Select(grant => grant.CapabilityId).Should().BeEquivalentTo(
            "generation.catalog.query", "generation.definition.validate", "generation.scaffold.generate");
        var generateScope = JsonNode.Parse(descriptor.CapabilityGrants.Single(grant => grant.CapabilityId == "generation.scaffold.generate").Scope.GetRawText())!.AsObject();
        generateScope["output_slot"]!.GetValue<string>().Should().Be(task.TaskId);
        generateScope["package_name"]!.GetValue<string>().Should().Be("ContactsDemo");
        generateScope["max_pages"]!.GetValue<int>().Should().Be(12);
        generateScope["package"]!.GetValue<string>().Should().Be("definition-site-v1");

        // ── handoff 帶淨化過的 generation_request ──
        var handoffJson = _fixture.FindContextEntries($"hlm.handoff.{task.TaskId}").Last().ContentRef;
        var generationRequest = JsonNode.Parse(handoffJson)!["generation_request"]!.AsObject();
        generationRequest["kind"]!.GetValue<string>().Should().Be("system_scaffold_generation");
        generationRequest.ToJsonString().Should().NotContain("hlm.").And.NotContain(":\\\\").And.NotContain(Paths(userId).AccessRoot.Replace("\\", "\\\\"));

        // ── broker 程序內沒有寫出任何檔案；draft 已刪除 ──
        var paths = Paths(userId);
        var projectRoot = Path.Combine(paths.ProjectsRoot, "ContactsDemo");
        (Directory.Exists(projectRoot) ? Directory.EnumerateFileSystemEntries(projectRoot, "*", SearchOption.AllDirectories) : Array.Empty<string>())
            .Should().BeEmpty("the broker writes no project files in Governed mode");
        Directory.EnumerateFiles(paths.DocumentsRoot).Should().BeEmpty();
        Coordinator.GetLineDraft(userId).Should().BeNull();

        // ── 代理容器：以註冊憑證啟動，AGENT_RUN 帶工作項 ──
        var spawn = _fixture.Containers.Spawned.Single(request => request.TrustedEnvironment["BROKER_TASK_ID"] == task.TaskId);
        spawn.WorkerType.Should().Be("agent");
        spawn.TrustedEnvironment["BROKER_PRINCIPAL_ID"].Should().Be(task.AssignedPrincipalId);
        spawn.TrustedEnvironment["BROKER_ROLE_ID"].Should().Be("role_executor");
        spawn.TrustedEnvironment["AGENT_MAX_ITERATIONS"].Should().Be("12");
        spawn.TrustedEnvironment["AGENT_RUN"].Should().Contain("WORK_ITEM_JSON").And.NotContain(paths.AccessRoot);
        System.Text.Encoding.UTF8.GetByteCount(spawn.TrustedEnvironment["AGENT_RUN"]).Should().BeLessThan(4096);
        spawn.TrustedEnvironment.Values.Should().NotContain(value => value.Contains(paths.AccessRoot, StringComparison.OrdinalIgnoreCase));
        var secret = spawn.SecretEnvironment["BROKER_REGISTRATION_SECRET"];

        // ── 代理 session：catalog → validate → generate ──
        var registered = await _client.RegisterAsync(task.AssignedPrincipalId!, task.TaskId, remoteAddress: LoopbackAddress, registrationSecret: secret);
        registered.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", registered);
        var session = registered.Session!;

        var catalog = await SubmitAsync(session, "generation.catalog.query", "query_component_catalog", new { section = "overview" });
        BrokerJson.ReadString(catalog.Body, "data", "execution_state").Should().Be("Succeeded", "the catalog response was {0}", catalog);
        var validate = await SubmitAsync(session, "generation.definition.validate", "validate_definition", new { template = GoldenLikeTemplate() });
        BrokerJson.ReadString(validate.Body, "data", "execution_state").Should().Be("Succeeded", "the validate response was {0}", validate);

        var generate = await SubmitAsync(session, "generation.scaffold.generate", "generate_scaffold", new
        {
            template = GoldenLikeTemplate(),
            title = "Contacts",
            out_dir = "C:/elsewhere",
            output_slot = "someone_else"
        });
        BrokerJson.ReadString(generate.Body, "data", "execution_state").Should().Be("Succeeded", "the generate response was {0}", generate);
        var requestId = BrokerJson.ReadString(generate.Body, "data", "request_id")!;
        var resultPayload = JsonNode.Parse(BrokerJson.ReadString(generate.Body, "data", "result_payload")!)!.AsObject();
        resultPayload["evidence_ref"]!.GetValue<string>().Should().Be($"generation.execution.{requestId}");
        resultPayload["zip"]!["path"]!.GetValue<string>().Should().StartWith($"{task.TaskId}/{requestId}/");
        resultPayload.ToJsonString().Should().NotContain(_fixture.OutputRoot.Replace("\\", "\\\\"));

        var stored = _fixture.Db.Get<ExecutionRequest>(requestId)!;
        stored.ExecutionState.Should().Be(ExecutionState.Succeeded);
        stored.EvidenceRef.Should().Be($"generation.execution.{requestId}");
        _fixture.FindContextEntries(stored.EvidenceRef!).Single().TaskId.Should().Be(task.TaskId);
        var auditTrail = _fixture.Db.Query<AuditEvent>(
                "SELECT * FROM audit_events WHERE trace_id = @traceId ORDER BY event_id",
                new { traceId = stored.TraceId })
            .Select(item => item.EventType)
            .Where(type => type.StartsWith("EXECUTION_", StringComparison.Ordinal))
            .ToList();
        auditTrail.Should().ContainInOrder("EXECUTION_RECEIVED", "EXECUTION_DISPATCHED", "EXECUTION_SUCCEEDED");

        // worker（假的）只照 grant scope 寫：args 中的路徑與 slot 都沒有被採用。
        Directory.EnumerateDirectories(_fixture.OutputRoot).Select(Path.GetFileName).Should().Contain(task.TaskId).And.NotContain("someone_else");

        // ── 交付：產物紀錄（portal 產物清單）、LINE 通知、任務 Completed、代理撤銷、容器停止 ──
        await _fixture.Services.GetRequiredService<GenerationDeliveryService>().ProcessPendingAsync();

        var workspace = _fixture.Services.GetRequiredService<HighLevelLineWorkspaceService>();
        var artifact = workspace.ListArtifacts(userId).Single(item => item.RelatedTaskId == task.TaskId);
        artifact.FileName.Should().Be("ContactsDemo-scaffold.zip");
        artifact.Source.Should().Be("governed_generation");
        File.Exists(Path.Combine(paths.DocumentsRoot, "ContactsDemo-scaffold.zip")).Should().BeTrue();
        _fixture.FindContextEntries($"hlm.notify.line.{artifact.NotificationId}").Should().NotBeEmpty();

        _fixture.FindTask(task.TaskId)!.State.Should().Be(TaskState.Completed);
        _fixture.Db.Get<Principal>(task.AssignedPrincipalId!)!.Status.Should().Be(EntityStatus.Disabled);
        _fixture.FindSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        var run = _fixture.Services.GetRequiredService<GovernedGenerationRunStore>().Get(task.TaskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Delivered);
        run.ContainerId.Should().NotBeNullOrWhiteSpace();
        _fixture.Containers.Stopped.Should().Contain(run.ContainerId);

        // 代理已撤銷：再送 generate 被拒。
        var afterwards = await SubmitAsync(session, "generation.scaffold.generate", "generate_scaffold", new { template = GoldenLikeTemplate() });
        afterwards.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the revoked agent's request was {0}", afterwards);
    }

    [Fact]
    public async Task SessionWithoutGenerationGrants_IsDeniedGenerate()
    {
        var session = _fixture.OpenSeededSession(_client, "role_executor", "system_scaffold");
        var dispatchedBefore = _fixture.Dispatcher.Dispatched.Count;

        var reply = await SubmitAsync(session, "generation.scaffold.generate", "generate_scaffold", new { template = GoldenLikeTemplate() });

        BrokerJson.ReadString(reply.Body, "data", "execution_state").Should().Be("Denied", "the response was {0}", reply);
        BrokerJson.ReadString(reply.Body, "data", "policy_reason").Should().Contain("No active grant");
        _fixture.Dispatcher.Dispatched.Count.Should().Be(dispatchedBefore);
    }

    [Theory]
    [InlineData("no-worker")]
    [InlineData("no-runtime")]
    public async Task NotReady_FailsClosed_WithoutATaskOrAnAgent(string missing)
    {
        var userId = await NewProductionUserAsync($"line-gengov-{missing}");
        await SendLineAsync(userId, "/建立 完整系統雛形 #NotReadyDemo");
        var spawnsBefore = _fixture.Containers.Spawned.Count;
        if (missing == "no-worker")
            _fixture.Registry.Registered = false;
        else
            _fixture.Containers.RuntimeAvailable = false;

        var confirmed = await SendLineAsync(userId, "y");

        ReadString(confirmed, "error").Should().Be("generation_unavailable");
        ReadString(confirmed, "reply").Should().Contain("暫不可用");
        IsNullOrMissing(confirmed, "created_task").Should().BeTrue();
        TasksSubmittedBy(userId).Should().BeEmpty("no task is created when governed generation is not ready");
        _fixture.Containers.Spawned.Count.Should().Be(spawnsBefore);
        Coordinator.GetLineDraft(userId).Should().NotBeNull("the draft stays so the user can try again");
        Directory.Exists(Path.Combine(Paths(userId).ProjectsRoot, "NotReadyDemo")).Should().BeFalse("no in-process generation happens");

        // 就緒之後同一個 draft 回 y 就能啟動。
        _fixture.Registry.Registered = true;
        _fixture.Containers.RuntimeAvailable = true;
        var retried = await SendLineAsync(userId, "y");
        IsNullOrMissing(retried, "error").Should().BeTrue("the retry reply was {0}", retried);
        ReadString(retried, "reply").Should().Contain("已受理");
    }

    [Fact]
    public async Task AgentLaunchFailure_FailsTheTask_AndKeepsTheDraftAndTheName()
    {
        var userId = await NewProductionUserAsync("line-gengov-spawnfail");
        await SendLineAsync(userId, "/建立 完整系統雛形 #SpawnFailDemo");
        _fixture.Containers.RefuseSpawns = true;

        var confirmed = await SendLineAsync(userId, "y");

        ReadString(confirmed, "error").Should().Be("generation_launch_failed");
        ReadString(confirmed, "reply").Should().Contain("暫不可用").And.NotContain("Max containers");
        var failedTask = TasksSubmittedBy(userId).Single();
        failedTask.State.Should().Be(TaskState.Failed);
        _fixture.Db.Get<Principal>(failedTask.AssignedPrincipalId!)!.Status.Should().Be(EntityStatus.Disabled);
        Coordinator.GetLineDraft(userId).Should().NotBeNull();
        Directory.Exists(Path.Combine(Paths(userId).ProjectsRoot, "SpawnFailDemo")).Should().BeFalse("the empty project folder is released");

        _fixture.Containers.RefuseSpawns = false;
        var retried = await SendLineAsync(userId, "y");
        ReadString(retried, "reply").Should().Contain("已受理");
        TasksSubmittedBy(userId).Should().HaveCount(2);
    }

    private async Task<(string UserId, BrokerTask Task, ContainerSpawnRequest Spawn)> StartGovernedRunAsync(string prefix, string projectName)
    {
        var userId = await NewProductionUserAsync(prefix);
        await SendLineAsync(userId, $"/建立 完整系統雛形 聯絡人管理 #{projectName}");
        var confirmed = await SendLineAsync(userId, "y");
        IsNullOrMissing(confirmed, "error").Should().BeTrue("the confirm reply was {0}", confirmed);
        var task = TasksSubmittedBy(userId).Single();
        var spawn = _fixture.Containers.Spawned.Single(request => request.TrustedEnvironment["BROKER_TASK_ID"] == task.TaskId);
        return (userId, task, spawn);
    }

    private static IEnumerable<string> HostPathForms(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return new[] { trimmed, trimmed.Replace('\\', '/'), trimmed.Replace("\\", "\\\\") };
    }

    [Fact]
    public async Task GovernedDraftPreview_DescribesTheFrontEndPrototype_WithoutTheLegacyStack()
    {
        var userId = await NewProductionUserAsync("line-gengov-preview");
        var draft = await SendLineAsync(userId, "/建立 完整系統雛形 聯絡人管理 需要 API 後端與登入 #PreviewDemo");
        var refined = await SendLineAsync(userId, "列表要能依類別篩選");

        foreach (var reply in new[] { ReadString(draft, "reply")!, ReadString(refined, "reply")! })
        {
            reply.Should().Contain(HighLevelSystemScaffoldService.GovernedProductDescription);
            foreach (var legacy in new[] { "frontend:", "backend:", "database:", "auth:", "package_format:", "aspnet_core_api", "ASP.NET Core", "SQLite" })
                reply.Should().NotContain(legacy);
        }
    }

    [Fact]
    public async Task CancelledInterview_AfterApproval_StartsNoAgentWhenTheUserRepliesYes()
    {
        var userId = await NewProductionUserAsync("line-gengov-ok-cancel");
        await SendLineAsync(userId, "/proj");
        await SendLineAsync(userId, $"#CancelPortal{Guid.NewGuid():N}");
        await SendLineAsync(userId, "2");
        await SendLineAsync(userId, "3");
        var approved = await SendLineAsync(userId, "/ok");
        IsNullOrMissing(approved, "error").Should().BeTrue("the approve reply was {0}", approved);
        Coordinator.GetLineDraft(userId).Should().NotBeNull();
        var spawnsBefore = _fixture.Containers.Spawned.Count;

        var cancelled = await SendLineAsync(userId, "/cancel");
        ReadString(cancelled, "reply").Should().Contain("撤下");
        var confirmed = await SendLineAsync(userId, "y");

        IsNullOrMissing(confirmed, "created_task").Should().BeTrue();
        TasksSubmittedBy(userId).Should().BeEmpty();
        _fixture.Containers.Spawned.Count.Should().Be(spawnsBefore, "no agent starts for a cancelled design");
    }

    [Fact]
    public async Task ConcurrentConfirmations_OfOneDraft_CreateOneTaskAndOneAgent()
    {
        var userId = await NewProductionUserAsync("line-gengov-race");
        await SendLineAsync(userId, "/建立 完整系統雛形 #RaceDemo");
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Containers.ResetRuntimeCheckStarted();
        _fixture.Containers.HoldRuntimeChecks = hold;

        // 第一個 y 停在就緒檢查（名稱重查之後、建立專案資料夾與任務之前）時送出第二個 y：
        // 第二個必須等第一個結束，看到 draft 已被用掉，不再建立任務。
        var first = SendLineAsync(userId, "y");
        await _fixture.Containers.RuntimeCheckStarted.WaitAsync(TimeSpan.FromSeconds(30));
        var second = SendLineAsync(userId, "y");
        await Task.Delay(300);
        _fixture.Containers.HoldRuntimeChecks = null;
        hold.SetResult();
        var replies = await Task.WhenAll(first, second);

        var task = TasksSubmittedBy(userId).Should().ContainSingle("one draft is confirmed once").Subject;
        _fixture.Containers.Spawned.Count(request => request.TrustedEnvironment["BROKER_TASK_ID"] == task.TaskId).Should().Be(1);
        replies.Count(reply => ReadString(reply, "reply")?.Contains("已受理") == true).Should().Be(1);
        replies.Select(reply => ReadString(reply, "error")).Should().Contain("draft_not_pending");
        Coordinator.GetLineDraft(userId).Should().BeNull();
    }

    [Fact]
    public async Task UserWithAGenerationInProgress_IsAskedToWait_WithoutATask()
    {
        var (userId, _, _) = await StartGovernedRunAsync("line-gengov-busyuser", "FirstRunDemo");
        await SendLineAsync(userId, "/建立 完整系統雛形 #SecondRunDemo");
        var spawnsBefore = _fixture.Containers.Spawned.Count;

        var second = await SendLineAsync(userId, "y");

        ReadString(second, "error").Should().Be("generation_in_progress");
        ReadString(second, "reply").Should().Contain("正在進行");
        IsNullOrMissing(second, "created_task").Should().BeTrue();
        TasksSubmittedBy(userId).Should().ContainSingle("the second request creates no task");
        _fixture.Containers.Spawned.Count.Should().Be(spawnsBefore);
        Coordinator.GetLineDraft(userId).Should().NotBeNull("the draft stays for a later try");
        Directory.Exists(Path.Combine(Paths(userId).ProjectsRoot, "SecondRunDemo")).Should().BeFalse();
    }

    [Fact]
    public async Task GlobalLimit_RejectsNewGenerations_UntilARunEnds()
    {
        await StartGovernedRunAsync("line-gengov-cap-a", "CapDemoA");
        var options = _fixture.Services.GetRequiredService<GovernedGenerationOptions>();
        var runs = _fixture.Services.GetRequiredService<GovernedGenerationRunStore>();
        var saved = options.MaxConcurrentRuns;
        options.MaxConcurrentRuns = runs.ListOpen().Count;
        try
        {
            var userId = await NewProductionUserAsync("line-gengov-cap-b");
            await SendLineAsync(userId, "/建立 完整系統雛形 #CapDemoB");
            var spawnsBefore = _fixture.Containers.Spawned.Count;

            var rejected = await SendLineAsync(userId, "y");

            ReadString(rejected, "error").Should().Be("generation_busy");
            ReadString(rejected, "reply").Should().Contain("忙碌");
            TasksSubmittedBy(userId).Should().BeEmpty();
            _fixture.Containers.Spawned.Count.Should().Be(spawnsBefore);
            Coordinator.GetLineDraft(userId).Should().NotBeNull();

            options.MaxConcurrentRuns = runs.ListOpen().Count + 1;
            var accepted = await SendLineAsync(userId, "y");
            ReadString(accepted, "reply").Should().Contain("已受理");
        }
        finally
        {
            options.MaxConcurrentRuns = saved;
        }
    }

    [Fact]
    public async Task AgentSession_ReadsNoHostPathOrRequesterIdentity()
    {
        var (userId, task, spawn) = await StartGovernedRunAsync("line-gengov-redact", "RedactDemo");
        var accessRoot = Paths(userId).AccessRoot;
        var registered = await _client.RegisterAsync(
            task.AssignedPrincipalId!, task.TaskId, remoteAddress: LoopbackAddress,
            registrationSecret: spawn.SecretEnvironment["BROKER_REGISTRATION_SECRET"]);
        registered.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", registered);
        var session = registered.Session!;

        void AssertClean(BrokerReply reply, string what)
        {
            foreach (var form in HostPathForms(accessRoot))
                reply.Body.Should().NotContainEquivalentOf(form, $"{what} must not expose the managed root");
            reply.Body.Should().NotContain(userId, $"{what} must not expose the requesting user");
        }

        var query = await _client.SendEncryptedAsync(session, "/api/v1/tasks/query", new { task_id = task.TaskId }, session.ScopedToken);
        query.StatusCode.Should().Be(HttpStatusCode.OK, "the task query was {0}", query);
        BrokerJson.ReadString(query.Body, "data", "taskId").Should().Be(task.TaskId);
        AssertClean(query, "/tasks/query");
        BrokerJson.ReadString(query.Body, "data", "submittedBy").Should().BeEmpty("the agent is not the submitter");
        JsonNode.Parse(BrokerJson.ReadString(query.Body, "data", "runtimeDescriptor")!)!.AsObject()
            .Select(property => property.Key)
            .Should().BeEquivalentTo(new[] { "capability_grants", "generation" }, "the task row only carries what the agent needs");
        JsonNode.Parse(BrokerJson.ReadString(query.Body, "data", "scopeDescriptor")!)!.AsObject()
            .Select(property => property.Key)
            .Should().NotContain(new[] { "origin_user_id", "execution_intent_document", "path_scope" });

        var spec = await _client.SendEncryptedAsync(session, "/api/v1/runtime/spec", new { }, session.ScopedToken);
        spec.StatusCode.Should().Be(HttpStatusCode.OK, "the runtime spec was {0}", spec);
        AssertClean(spec, "/runtime/spec");

        var handoff = await _client.SendEncryptedAsync(session, "/api/v1/context/read", new { document_id = $"hlm.handoff.{task.TaskId}" }, session.ScopedToken);
        handoff.StatusCode.Should().Be(HttpStatusCode.NotFound, "the agent cannot read the handoff: {0}", handoff);
        var listed = await _client.SendEncryptedAsync(session, "/api/v1/context/list", new { task_id = task.TaskId }, session.ScopedToken);
        listed.StatusCode.Should().Be(HttpStatusCode.OK, "the context list was {0}", listed);
        listed.Body.Should().NotContain("hlm.handoff.");
        AssertClean(listed, "/context/list");

        var planId = _fixture.Db.Query<Plan>("SELECT * FROM plans WHERE task_id = @taskId", new { taskId = task.TaskId }).Single().PlanId;
        var plan = await _client.SendEncryptedAsync(session, "/api/v1/plans/get", new { plan_id = planId }, session.ScopedToken);
        plan.StatusCode.Should().Be(HttpStatusCode.OK, "the plan was {0}", plan);
        AssertClean(plan, "/plans/get");

        // broker 內部仍保有交付需要的資料：handoff 帶受管路徑，但只有系統讀得到；管理員看得到提交者。
        var handoffEntry = _fixture.FindContextEntries($"hlm.handoff.{task.TaskId}").Last();
        handoffEntry.Acl.Should().NotContain("\"*\"");
        handoffEntry.ContentRef.Should().Contain("ManagedPaths");
        var admin = _client.OpenSession("role_admin");
        var adminQuery = await _client.SendEncryptedAsync(admin, "/api/v1/tasks/query", new { task_id = task.TaskId }, admin.ScopedToken);
        BrokerJson.ReadString(adminQuery.Body, "data", "submittedBy").Should().Be($"line:{userId}");
    }

    [Fact]
    public async Task AdminStop_WithTheListedAgentId_StopsTheGovernedAgent()
    {
        var (userId, task, _) = await StartGovernedRunAsync("line-gengov-stop", "StopDemo");
        var admin = _client.OpenSession("role_admin");

        var list = await _client.SendEncryptedAsync(admin, "/api/v1/agents/list", new { }, admin.ScopedToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK, "the agent list was {0}", list);
        var agentId = task.TaskId[5..];
        list.Body.Should().Contain(agentId);

        var stop = await _client.SendEncryptedAsync(admin, "/api/v1/agents/stop", new { agent_id = agentId }, admin.ScopedToken);

        stop.StatusCode.Should().Be(HttpStatusCode.OK, "the stop response was {0}", stop);
        BrokerJson.ReadString(stop.Body, "data", "status").Should().Be("stopped");
        _fixture.FindTask(task.TaskId)!.State.Should().Be(TaskState.Failed);
        _fixture.Db.Get<Principal>(task.AssignedPrincipalId!)!.Status.Should().Be(EntityStatus.Disabled);
        var run = _fixture.Services.GetRequiredService<GovernedGenerationRunStore>().Get(task.TaskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        run.FailureReason.Should().Be("stopped_by_admin");
        _fixture.Containers.Stopped.Should().Contain(run.ContainerId);
        _fixture.Db.Query<SharedContextEntry>("SELECT * FROM shared_context_entries WHERE document_id LIKE 'hlm.notify.line.%'")
            .Select(entry => JsonNode.Parse(entry.ContentRef)!)
            .Should().Contain(notice => notice["UserId"]!.GetValue<string>() == userId);

        // 不是管理員就不能停。
        var agentSession = _fixture.OpenSeededSession(_client, "role_executor", "system_scaffold");
        var denied = await _client.SendEncryptedAsync(agentSession, "/api/v1/agents/stop", new { agent_id = agentId }, agentSession.ScopedToken);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<bool> ValidateSucceedsAsync(BrokerTestSession session)
    {
        var reply = await SubmitAsync(session, "generation.definition.validate", "validate_definition", new { template = GoldenLikeTemplate() });
        return reply.StatusCode == HttpStatusCode.OK &&
               BrokerJson.ReadString(reply.Body, "data", "execution_state") == "Succeeded";
    }

    private List<ContainerSession> ActiveSessionsOf(string taskId)
        => _fixture.Db.Query<ContainerSession>(
            "SELECT * FROM container_sessions WHERE task_id = @taskId AND status = @active",
            new { taskId, active = (int)SessionStatus.Active });

    [Fact]
    public async Task ReRegisteredAgent_GetsOnlyTheRemainingTaskQuota_AndTheOldSessionEnds()
    {
        var (_, task, spawn) = await StartGovernedRunAsync("line-gengov-quota", "QuotaDemo");
        var secret = spawn.SecretEnvironment["BROKER_REGISTRATION_SECRET"];
        var first = (await _client.RegisterAsync(task.AssignedPrincipalId!, task.TaskId, remoteAddress: LoopbackAddress, registrationSecret: secret)).Session!;
        for (var i = 0; i < 2; i++)
            (await ValidateSucceedsAsync(first)).Should().BeTrue();

        // 容器重啟後以同一把註冊憑證再註冊：validate 只剩 6 - 2 次，其他能力不變；舊 session 與它的授予結束。
        var again = await _client.RegisterAsync(task.AssignedPrincipalId!, task.TaskId, remoteAddress: LoopbackAddress, registrationSecret: secret);
        again.StatusCode.Should().Be(HttpStatusCode.OK, "the second register response was {0}", again);
        var grants = _fixture.Db.Query<CapabilityGrant>(
                "SELECT * FROM capability_grants WHERE session_id = @sessionId",
                new { sessionId = again.Session!.SessionId })
            .ToDictionary(grant => grant.CapabilityId, grant => grant.RemainingQuota);
        grants["generation.definition.validate"].Should().Be(4);
        grants["generation.catalog.query"].Should().Be(20);
        grants["generation.scaffold.generate"].Should().Be(2);

        _fixture.Db.Get<ContainerSession>(first.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        _fixture.Db.Query<CapabilityGrant>("SELECT * FROM capability_grants WHERE session_id = @sessionId", new { sessionId = first.SessionId })
            .Should().OnlyContain(grant => grant.Status == GrantStatus.Revoked);
        ActiveSessionsOf(task.TaskId).Select(session => session.SessionId).Should().Equal(again.Session!.SessionId);

        // 所有 session 合計不超過任務的 6 次：舊 session 再也用不到，新 session 用完剩下的 4 次就停。
        var succeeded = 2;
        for (var i = 0; i < 8; i++)
        {
            if (await ValidateSucceedsAsync(first)) succeeded++;
            if (await ValidateSucceedsAsync(again.Session!)) succeeded++;
        }

        succeeded.Should().Be(6);
    }

    [Fact]
    public async Task SessionsRegisteredBeforeAnyUse_ShareOneTaskQuota()
    {
        var (_, task, spawn) = await StartGovernedRunAsync("line-gengov-quota-many", "QuotaManyDemo");
        var secret = spawn.SecretEnvironment["BROKER_REGISTRATION_SECRET"];
        var sessions = new List<BrokerTestSession>();
        for (var i = 0; i < 3; i++)
        {
            var registered = await _client.RegisterAsync(task.AssignedPrincipalId!, task.TaskId, remoteAddress: LoopbackAddress, registrationSecret: secret);
            registered.StatusCode.Should().Be(HttpStatusCode.OK, "register {0} was {1}", i, registered);
            sessions.Add(registered.Session!);
        }

        // 一個任務同時只留最後註冊的 session；三個 session 輪流呼叫，合計仍只有 6 次成功。
        ActiveSessionsOf(task.TaskId).Select(session => session.SessionId).Should().Equal(sessions[^1].SessionId);
        var succeeded = 0;
        for (var round = 0; round < 8; round++)
        {
            foreach (var session in sessions)
            {
                if (await ValidateSucceedsAsync(session))
                    succeeded++;
            }
        }

        succeeded.Should().Be(6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModelRecommendation_IsUsed_OnlyWhenTheLlmProxyProviderServesIt(bool sameProvider)
    {
        var proxy = _fixture.Services.GetRequiredService<LlmProxyOptions>();
        _fixture.Planner.Recommendation = new HighLevelExecutionModelRequest
        {
            Alias = "execution-default",
            Model = "planner-recommended-model",
            Tier = "standard",
            Provider = sameProvider ? proxy.Provider : "another-provider",
            ValidationStatus = "validated"
        };

        var (_, task, spawn) = await StartGovernedRunAsync($"line-gengov-model-{sameProvider}", $"ModelDemo{sameProvider}");

        var descriptor = TaskRuntimeDescriptor.Parse(task.RuntimeDescriptor);
        if (sameProvider)
        {
            descriptor.Llm.DefaultModel.Should().Be("planner-recommended-model");
            spawn.TrustedEnvironment["AGENT_MODEL"].Should().Be("planner-recommended-model");
        }
        else
        {
            descriptor.Llm.DefaultModel.Should().BeEmpty("a model another provider serves is not written into the task");
            spawn.TrustedEnvironment["AGENT_MODEL"].Should().Be(proxy.DefaultModel);
        }
    }

    private static JsonObject GoldenTemplate()
        => JsonNode.Parse(File.ReadAllText(Path.Combine(
            InProcessGenerationWorker.FindRepositoryRoot(), "tools", "generation", "examples", "golden.definition-template.json")))!.AsObject();

    private static JsonObject ResultPayload(BrokerReply reply)
        => JsonNode.Parse(BrokerJson.ReadString(reply.Body, "data", "result_payload")!)!.AsObject();

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// 程序內端到端：使用者確認 → broker 啟動受控代理（假的容器管理器）→ 以代理的註冊憑證開 session，
    /// 依代理的工作流程呼叫三個能力，由 generation-worker 真正的 handler 與 repo 中的生成器 CLI 處理 golden 範例：
    /// catalog → validate（一次失敗、依結構化錯誤修正後通過）→ generate。
    /// 驗證 broker 的 ingest（路徑、sha256）、證據文件、稽核鏈、產物紀錄、LINE 通知、任務 Completed，
    /// 交付的 zip 與 worker 寫出的檔案、報告中的 manifest 逐檔一致，且 broker 沒有在專案資料夾寫入任何檔案。
    /// </summary>
    [Fact]
    public async Task ConfirmedScaffold_WithTheRealGenerationWorker_GeneratesVerifiesAndDeliversTheGoldenPrototype()
    {
        var worker = new InProcessGenerationWorker(_fixture.OutputRoot);
        var repositoryRoot = worker.Options.ToolsRoot;

        var userId = await NewProductionUserAsync("line-gengov-e2e");
        var draft = await SendLineAsync(userId, "/建立 完整系統雛形 聯絡人管理原型 #GoldenDemo");
        IsNullOrMissing(draft, "error").Should().BeTrue("the draft reply was {0}", draft);

        var confirmed = await SendLineAsync(userId, "y");
        IsNullOrMissing(confirmed, "error").Should().BeTrue("the confirm reply was {0}", confirmed);
        ReadString(confirmed, "reply")!.Should().Contain("已受理");

        var task = TasksSubmittedBy(userId).Single();
        var spawn = _fixture.Containers.Spawned.Single(request => request.TrustedEnvironment["BROKER_TASK_ID"] == task.TaskId);

        // 代理收到的工作項（AGENT_RUN 的 WORK_ITEM_JSON）：生成類型與上限，與 handoff 的 generation_request 相同。
        var agentRun = spawn.TrustedEnvironment["AGENT_RUN"];
        var workItemJson = agentRun[(agentRun.IndexOf("WORK_ITEM_JSON: ", StringComparison.Ordinal) + "WORK_ITEM_JSON: ".Length)..];
        var workItem = JsonNode.Parse(workItemJson)!.AsObject();
        workItem["kind"]!.GetValue<string>().Should().Be("system_scaffold_generation");
        workItem["limits"]!["max_pages"]!.GetValue<int>().Should().Be(12);
        workItem["limits"]!["page_types"]!.AsArray().Select(type => type!.GetValue<string>()).Should().Equal("list", "detail", "form");
        JsonNode.DeepEquals(workItem, JsonNode.Parse(_fixture.FindContextEntries($"hlm.handoff.{task.TaskId}").Last().ContentRef)!["generation_request"])
            .Should().BeTrue("the agent works from the same sanitized request the handoff records");

        var registered = await _client.RegisterAsync(
            task.AssignedPrincipalId!, task.TaskId, remoteAddress: LoopbackAddress,
            registrationSecret: spawn.SecretEnvironment["BROKER_REGISTRATION_SECRET"]);
        registered.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", registered);
        var session = registered.Session!;

        _fixture.Dispatcher.Worker = worker;

        // ── 1. 查型錄：overview 與 field_types，回傳內容與型錄 hash 一致 ──
        var overview = await SubmitAsync(session, "generation.catalog.query", "query_component_catalog", new { section = "overview" });
        BrokerJson.ReadString(overview.Body, "data", "execution_state").Should().Be("Succeeded", "the catalog response was {0}", overview);
        var overviewPayload = ResultPayload(overview);
        overviewPayload["ok"]!.GetValue<bool>().Should().BeTrue();
        overviewPayload["section"]!.GetValue<string>().Should().Be("overview");
        var catalogSha = overviewPayload["catalog_sha256"]!.GetValue<string>();
        catalogSha.Should().MatchRegex("^[0-9a-f]{64}$");

        var fieldTypes = await SubmitAsync(session, "generation.catalog.query", "query_component_catalog", new { section = "field_types" });
        BrokerJson.ReadString(fieldTypes.Body, "data", "execution_state").Should().Be("Succeeded", "the catalog response was {0}", fieldTypes);
        ResultPayload(fieldTypes)["catalog_sha256"]!.GetValue<string>().Should().Be(catalogSha);

        // ── 2. 驗證：生成器不支援的欄位型別 → 成功的呼叫、ok:false、結構化錯誤；修正後 ok:true ──
        var broken = GoldenTemplate();
        broken["definitions"]!["pages"]![2]!["definition"]!["fields"]![0]!["type"] = "slider";
        var rejected = await SubmitAsync(session, "generation.definition.validate", "validate_definition", new { template = broken });
        BrokerJson.ReadString(rejected.Body, "data", "execution_state").Should().Be("Succeeded", "the validate response was {0}", rejected);
        var rejectedPayload = ResultPayload(rejected);
        rejectedPayload["ok"]!.GetValue<bool>().Should().BeFalse();
        rejectedPayload["errors"]![0]!["code"]!.GetValue<string>().Should().Be("FIELD_TYPE_UNSUPPORTED");
        rejectedPayload["errors"]![0]!["path"]!.GetValue<string>().Should().Be("definitions.pages[2].definition.fields[0].type");

        var accepted = await SubmitAsync(session, "generation.definition.validate", "validate_definition", new { template = GoldenTemplate() });
        BrokerJson.ReadString(accepted.Body, "data", "execution_state").Should().Be("Succeeded", "the validate response was {0}", accepted);
        var acceptedPayload = ResultPayload(accepted);
        acceptedPayload["ok"]!.GetValue<bool>().Should().BeTrue("the validation result was {0}", acceptedPayload);
        var validationDigest = acceptedPayload["validation_digest"]!.GetValue<string>();

        // ── 3. 生成：args 中的輸出位置不被採用，產物寫在 grant scope 的 slot 之下 ──
        var generate = await SubmitAsync(session, "generation.scaffold.generate", "generate_scaffold", new
        {
            template = GoldenTemplate(),
            title = "Contacts prototype",
            out_dir = Path.Combine(_fixture.OutputRoot, "elsewhere"),
            output_slot = "someone_else"
        });
        BrokerJson.ReadString(generate.Body, "data", "execution_state").Should().Be("Succeeded", "the generate response was {0}", generate);
        var requestId = BrokerJson.ReadString(generate.Body, "data", "request_id")!;
        var generated = ResultPayload(generate);
        generated["ok"]!.GetValue<bool>().Should().BeTrue();
        generated["delivery"]!.GetValue<string>().Should().Be("queued");
        generated["evidence_ref"]!.GetValue<string>().Should().Be($"generation.execution.{requestId}");
        generated["validation_digest"]!.GetValue<string>().Should().Be(validationDigest);
        generated["catalog_sha256"]!.GetValue<string>().Should().Be(catalogSha);
        generated["pages"]!.AsArray().Select(page => page!["type"]!.GetValue<string>()).Should().Equal("list", "detail", "form");
        var zipRelativePath = generated["zip"]!["path"]!.GetValue<string>();
        zipRelativePath.Should().Be($"{task.TaskId}/{requestId}/GoldenDemo-scaffold.zip");
        var zipSha = generated["zip"]!["sha256"]!.GetValue<string>();
        foreach (var hostPath in new[] { _fixture.OutputRoot, repositoryRoot, Paths(userId).AccessRoot })
        {
            generate.Body.Should().NotContain(hostPath.Replace("\\", "\\\\")).And.NotContain(hostPath.Replace('\\', '/'));
        }

        Directory.EnumerateDirectories(_fixture.OutputRoot).Select(Path.GetFileName).Should().NotContain(new[] { "elsewhere", "someone_else" });
        var workerZip = Path.Combine(_fixture.OutputRoot, zipRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Sha256Hex(File.ReadAllBytes(workerZip)).Should().Be(zipSha);

        // ── ingest：執行請求 Succeeded、evidenceRef、稽核鏈；證據文件屬於該任務、由 ingestor 寫入 ──
        var stored = _fixture.Db.Get<ExecutionRequest>(requestId)!;
        stored.ExecutionState.Should().Be(ExecutionState.Succeeded);
        stored.EvidenceRef.Should().Be($"generation.execution.{requestId}");
        _fixture.Db.Query<AuditEvent>(
                "SELECT * FROM audit_events WHERE trace_id = @traceId ORDER BY event_id",
                new { traceId = stored.TraceId })
            .Select(item => item.EventType)
            .Where(type => type.StartsWith("EXECUTION_", StringComparison.Ordinal))
            .Should().ContainInOrder("EXECUTION_RECEIVED", "EXECUTION_DISPATCHED", "EXECUTION_SUCCEEDED");

        var evidenceEntry = _fixture.FindContextEntries(stored.EvidenceRef!).Single();
        evidenceEntry.TaskId.Should().Be(task.TaskId);
        evidenceEntry.AuthorPrincipalId.Should().Be(GenerationPackageIngestor.Author);
        var evidence = JsonNode.Parse(evidenceEntry.ContentRef)!.AsObject();
        evidence["zip"]!["sha256"]!.GetValue<string>().Should().Be(zipSha);
        evidence["zip"]!["path"]!.GetValue<string>().Should().Be(zipRelativePath);
        evidence["validation_digest"]!.GetValue<string>().Should().Be(validationDigest);
        evidence["delivered_file_name"]!.GetValue<string>().Should().Be("GoldenDemo-scaffold.zip");

        // ── 交付：產物紀錄、LINE 通知、任務 Completed、代理撤銷、容器停止 ──
        await _fixture.Services.GetRequiredService<GenerationDeliveryService>().ProcessPendingAsync();

        var paths = Paths(userId);
        var workspace = _fixture.Services.GetRequiredService<HighLevelLineWorkspaceService>();
        var artifact = workspace.ListArtifacts(userId).Single(item => item.RelatedTaskId == task.TaskId);
        artifact.Success.Should().BeTrue();
        artifact.Source.Should().Be("governed_generation");
        artifact.FileName.Should().Be("GoldenDemo-scaffold.zip");
        var notification = _fixture.FindContextEntries($"hlm.notify.line.{artifact.NotificationId}");
        notification.Should().NotBeEmpty();
        var notice = JsonNode.Parse(notification.Last().ContentRef)!.AsObject();
        notice["Title"]!.GetValue<string>().Should().Be("系統雛形已生成");
        notice["UserId"]!.GetValue<string>().Should().Be(userId);

        _fixture.FindTask(task.TaskId)!.State.Should().Be(TaskState.Completed);
        _fixture.Db.Get<Principal>(task.AssignedPrincipalId!)!.Status.Should().Be(EntityStatus.Disabled);
        _fixture.FindSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        var run = _fixture.Services.GetRequiredService<GovernedGenerationRunStore>().Get(task.TaskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Delivered);
        run.ZipSha256.Should().Be(zipSha);
        _fixture.Containers.Stopped.Should().Contain(run.ContainerId);

        // ── 交付的 zip：與 worker 寫出的檔案相同，內容與報告中的 manifest 逐檔一致 ──
        var deliveredZip = Path.Combine(paths.DocumentsRoot, "GoldenDemo-scaffold.zip");
        Sha256Hex(File.ReadAllBytes(deliveredZip)).Should().Be(zipSha);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(deliveredZip))
        {
            var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            entries.Keys.Should().Contain(new[] { "site/index.html", "site/boot.js", "site/README.txt", "site/definition-template.json", "report/validation.json", "report/manifest.json" });
            entries.Keys.Should().Contain(new[] { "site/definitions/contacts-list.json", "site/definitions/contact-detail.json", "site/definitions/contact-form.json" });
            entries.Keys.Should().OnlyContain(name => name.StartsWith("site/", StringComparison.Ordinal) || name.StartsWith("report/", StringComparison.Ordinal));

            JsonObject manifest;
            using (var reader = new StreamReader(entries["report/manifest.json"].Open()))
                manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            manifest["format"]!.GetValue<string>().Should().Be("definition-site-v1");
            manifest["catalog_sha256"]!.GetValue<string>().Should().Be(catalogSha);
            manifest["validation_digest"]!.GetValue<string>().Should().Be(validationDigest);

            var listed = manifest["files"]!.AsArray().Select(file => file!.AsObject()).ToList();
            listed.Select(file => file["path"]!.GetValue<string>()).Append("report/manifest.json")
                .Should().BeEquivalentTo(entries.Keys, "the zip holds exactly the files the manifest lists");
            foreach (var file in listed)
            {
                using var stream = new MemoryStream();
                using (var entryStream = entries[file["path"]!.GetValue<string>()].Open())
                    entryStream.CopyTo(stream);
                var bytes = stream.ToArray();
                Sha256Hex(bytes).Should().Be(file["sha256"]!.GetValue<string>(), $"{file["path"]} matches the manifest");
                bytes.LongLength.Should().Be(file["size"]!.GetValue<long>());
            }

            generated["file_count"]!.GetValue<int>().Should().Be(entries.Count);
        }

        // ── broker 程序沒有寫出專案檔案：專案資料夾仍是空的，文件區只有交付的 zip ──
        var projectRoot = Path.Combine(paths.ProjectsRoot, "GoldenDemo");
        (Directory.Exists(projectRoot) ? Directory.EnumerateFileSystemEntries(projectRoot, "*", SearchOption.AllDirectories) : Array.Empty<string>())
            .Should().BeEmpty("the broker writes no project files in Governed mode");
        Directory.EnumerateFiles(paths.DocumentsRoot).Select(Path.GetFileName).Should().Equal("GoldenDemo-scaffold.zip");
    }
}
