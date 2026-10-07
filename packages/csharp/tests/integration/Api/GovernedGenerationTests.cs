using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
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
/// - 代理啟動失敗時任務標為 Failed，draft 保留，專案名稱可再用。
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
}
