using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;
using BrokerCore;
using BrokerCore.Contracts;
using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Broker;

/// <summary>
/// broker 收下 generation-worker 產物的檢查（GenerationIngestingDispatcher／GenerationPackageIngestor）：
/// 路徑逃逸、符號連結、sha256 不符、檔案遺失、slot 或請求 id 不符都會讓這次執行失敗，而且不交付
/// （使用者文件區沒有檔案、執行紀錄維持 running、沒有證據文件、不通知交付服務）。
/// </summary>
public sealed class GenerationIngestTests : IDisposable
{
    private readonly GovernedGenerationTestSupport _env = new();

    public void Dispose() => _env.Dispose();

    private GenerationPackageIngestor Ingestor()
        => new(_env.Options, _env.Runs, _env.Workspace, _env.Db, _env.Signal, NullLogger<GenerationPackageIngestor>.Instance);

    private static ApprovedRequest Request(string taskId, string requestId, string? scope = null) => new()
    {
        RequestId = requestId,
        CapabilityId = GenerationCapabilities.ScaffoldGenerate,
        Route = GenerationCapabilities.GenerateRoute,
        Payload = """{"route":"generate_scaffold","args":{"template":{}}}""",
        Scope = scope ?? GovernedGenerationTestSupport.Scope(taskId),
        TaskId = taskId,
        PrincipalId = GovernedGenerationLauncher.BuildPrincipalId(taskId),
        SessionId = "ses_test"
    };

    private static ExecutionResult WorkerOk(string requestId, string payload)
        => new() { RequestId = requestId, Success = true, ResultPayload = payload, AnsweredByWorker = true };

    /// <summary>內層分派器：回傳預先設定的 worker 結果並記錄呼叫次數。</summary>
    private sealed class StubDispatcher : IExecutionDispatcher
    {
        private readonly Func<ApprovedRequest, ExecutionResult> _respond;

        public StubDispatcher(Func<ApprovedRequest, ExecutionResult> respond) => _respond = respond;

        public int Calls { get; private set; }

        public Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
        {
            Calls++;
            return Task.FromResult(_respond(approvedRequest));
        }
    }

    private void AssertNotDelivered(string taskId, string requestId)
    {
        Directory.EnumerateFiles(_env.DocumentsRoot).Should().BeEmpty("a refused package is never copied to the user");
        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Running);
        _env.Db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE document_id = @id",
                new { id = GovernedGenerationRunStore.BuildEvidenceDocumentId(requestId) })
            .Should().BeEmpty("no evidence is written for a refused package");
        _env.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None).Result.Should().BeFalse("delivery is not woken up");
    }

    [Fact]
    public async Task ValidPackage_IsCopiedToTheUser_WithEvidence_AndQueuedForDelivery()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        var inner = new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size)));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.EvidenceRef.Should().Be($"generation.execution.{requestId}");
        result.AnsweredByWorker.Should().BeTrue();
        inner.Calls.Should().Be(1);

        var payload = JsonNode.Parse(result.ResultPayload!)!.AsObject();
        payload["zip"]!["path"]!.GetValue<string>().Should().Be(zipPath);
        payload["zip"]!["sha256"]!.GetValue<string>().Should().Be(sha);
        payload["delivery"]!.GetValue<string>().Should().Be("queued");
        result.ResultPayload.Should().NotContain(_env.Root).And.NotContain(_env.Root.Replace("\\", "\\\\"));

        var delivered = Path.Combine(_env.DocumentsRoot, "ContactsDemo-scaffold.zip");
        File.ReadAllText(delivered).Should().Be("PK-fake-package");

        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Ingested);
        run.RequestId.Should().Be(requestId);
        run.ZipSha256.Should().Be(sha);
        run.DeliveredFilePath.Should().Be(delivered);
        run.PageCount.Should().Be(3);

        var evidence = _env.Db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @id",
            new { id = result.EvidenceRef }).Single();
        evidence.TaskId.Should().Be(taskId);
        evidence.AuthorPrincipalId.Should().Be("system:generation-ingestor");
        evidence.ContentRef.Should().Contain(sha).And.NotContain(_env.Root.Replace("\\", "\\\\"));
        SystemContextDocuments.IsReservedDocumentId(evidence.DocumentId).Should().BeTrue();

        (await _env.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue("delivery is woken up");
    }

    [Fact]
    public async Task OtherRoutes_PassThroughUntouched()
    {
        var inner = new StubDispatcher(request => WorkerOk(request.RequestId, "{\"content\":\"x\"}"));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor());

        var result = await dispatcher.DispatchAsync(new ApprovedRequest
        {
            RequestId = "req_other",
            CapabilityId = GenerationCapabilities.DefinitionValidate,
            Route = GenerationCapabilities.ValidateRoute,
            Scope = "{\"routes\":[\"validate_definition\"]}",
            TaskId = "task_other"
        });

        result.Success.Should().BeTrue();
        result.ResultPayload.Should().Be("{\"content\":\"x\"}");
        inner.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("../{slot}/{req}/ContactsDemo-scaffold.zip")]
    [InlineData("{slot}/../{slot}/{req}/ContactsDemo-scaffold.zip")]
    [InlineData("{slot}/{req}/../../escape-scaffold.zip")]
    [InlineData("/{slot}/{req}/ContactsDemo-scaffold.zip")]
    [InlineData("{slot}\\{req}\\ContactsDemo-scaffold.zip")]
    [InlineData("C:/{slot}/{req}/ContactsDemo-scaffold.zip")]
    [InlineData("{slot}/{req}/nested/ContactsDemo-scaffold.zip")]
    [InlineData("{slot}/{req}/ContactsDemo.zip")]
    [InlineData("other_slot/{req}/ContactsDemo-scaffold.zip")]
    [InlineData("{slot}/req_other/ContactsDemo-scaffold.zip")]
    public async Task EscapingOrForeignZipPaths_AreRefused_AndNotDelivered(string template)
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (_, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        var zipPath = template.Replace("{slot}", taskId).Replace("{req}", requestId);
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Generated package was not accepted");
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task DigestMismatch_IsRefused_AndNotDelivered()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (zipPath, _, size) = _env.WriteWorkerPackage(taskId, requestId);
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, new string('0', 64), size))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sha256");
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task SizeMismatch_IsRefused_AndNotDelivered()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size + 1))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task MissingFile_IsRefused_AndNotDelivered()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        File.Delete(Path.Combine(_env.OutputRoot, zipPath.Replace('/', Path.DirectorySeparatorChar)));
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("missing");
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task ReportedSlotDifferentFromTheGrantScope_IsRefused_AndNotDelivered()
    {
        var taskId = _env.SeedRun();
        var otherTaskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        // worker 把產物寫到另一個任務的 slot，並如實回報那個 slot。
        var (zipPath, sha, size) = _env.WriteWorkerPackage(otherTaskId, requestId);
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(otherTaskId, requestId, zipPath, sha, size))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("output slot");
        AssertNotDelivered(taskId, requestId);
        _env.Runs.Get(otherTaskId)!.Status.Should().Be(GovernedGenerationRunStatus.Running);
    }

    [Fact]
    public async Task ReportedRequestIdDifferentFromTheDispatch_IsRefused()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var otherRequestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, otherRequestId);
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, otherRequestId, zipPath, sha, size))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task GrantScopeForAnotherTask_IsRefusedBeforeDispatch()
    {
        var taskId = _env.SeedRun();
        var otherTaskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var inner = new StubDispatcher(_ => throw new InvalidOperationException("must not dispatch"));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId, GovernedGenerationTestSupport.Scope(otherTaskId)));

        result.Success.Should().BeFalse();
        inner.Calls.Should().Be(0);
        AssertNotDelivered(taskId, requestId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"routes":["generate_scaffold"],"output_slot":"../x"}""")]
    [InlineData("""{"routes":["generate_scaffold"],"output_slot":"task_x\n"}""")]
    [InlineData("not json")]
    public async Task GrantScopeWithoutAValidSlot_IsRefusedBeforeDispatch(string scope)
    {
        var taskId = _env.SeedRun();
        var inner = new StubDispatcher(_ => throw new InvalidOperationException("must not dispatch"));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, IdGen.New("req"), scope));

        result.Success.Should().BeFalse();
        inner.Calls.Should().Be(0);
    }

    /// <summary>
    /// 名稱的格式檢查要涵蓋整個字串：.NET 的 <c>$</c> 也接受結尾的換行，結尾帶換行的 slot、請求 id 或 zip 檔名都不是安全的路徑段。
    /// </summary>
    [Theory]
    [InlineData("task_slot\n", "req_1", "task_slot\n/req_1/ContactsDemo-scaffold.zip")]
    [InlineData("task_slot", "req_1\n", "task_slot/req_1\n/ContactsDemo-scaffold.zip")]
    [InlineData("task_slot", "req_1", "task_slot/req_1/ContactsDemo-scaffold.zip\n")]
    public void NamesWithATrailingNewline_AreNotSafePathSegments(string slot, string requestId, string zipPath)
    {
        GenerationPackageIngestor.TryResolvePackage(_env.OutputRoot, slot, requestId, zipPath, out var packagePath, out var error)
            .Should().BeFalse();
        packagePath.Should().BeEmpty();
        error.Should().Be("the zip path is not under this request's output slot");

        GenerationPackageIngestor.TryResolvePackage(_env.OutputRoot, "task_slot", "req_1", "task_slot/req_1/ContactsDemo-scaffold.zip", out _, out _)
            .Should().BeTrue("the same names without the newline are accepted");
    }

    [Fact]
    public void ScopeSlotWithATrailingNewline_IsNotAValidSlot()
    {
        GenerationPackageIngestor.TryReadScopeSlot("""{"output_slot":"task_slot\n"}""", out _).Should().BeFalse();
        GenerationPackageIngestor.TryReadScopeSlot("""{"output_slot":"task_slot"}""", out var slot).Should().BeTrue();
        slot.Should().Be("task_slot");
    }

    /// <summary>
    /// 任務在 ingest 途中結束（例如 watchdog 期限或管理員停止，發生在複製之後、狀態轉換之前）：
    /// 這次執行失敗、已複製的檔案移除、不通知交付，也不留下宣稱「已排入交付」的證據。
    /// </summary>
    [Fact]
    public async Task RunEndingDuringIngest_IsRefused_WithoutEvidence()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        var ingestor = Ingestor();
        var copiedBeforeTransition = false;
        ingestor.BeforeRunTransitionForTesting = endingTaskId =>
        {
            copiedBeforeTransition = Directory.EnumerateFiles(_env.DocumentsRoot).Any();
            _env.Runs.TryUpdate(endingTaskId, "system:test", run =>
            {
                run.Status = GovernedGenerationRunStatus.Failed;
                run.FailureReason = "deadline_exceeded";
                return true;
            }).Should().BeTrue();
        };
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size))),
            ingestor);

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        copiedBeforeTransition.Should().BeTrue("the hook runs after the copy, where the race happens");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("the task ended while the package was being verified");
        result.EvidenceRef.Should().BeNullOrEmpty();
        Directory.EnumerateFiles(_env.DocumentsRoot).Should().BeEmpty("the copied package is removed");
        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        run.EvidenceDocumentId.Should().BeEmpty();
        _env.Db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE document_id = @id",
                new { id = GovernedGenerationRunStore.BuildEvidenceDocumentId(requestId) })
            .Should().BeEmpty("no evidence claims a queued delivery for a refused package");
        (await _env.Signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse("delivery is not woken up");
    }

    [Fact]
    public async Task SecondPackageForTheSameTask_IsRefused()
    {
        var taskId = _env.SeedRun();
        var firstRequest = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, firstRequest);
        var first = await new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(firstRequest, GovernedGenerationTestSupport.WorkerPayload(taskId, firstRequest, zipPath, sha, size))),
            Ingestor()).DispatchAsync(Request(taskId, firstRequest));
        first.Success.Should().BeTrue(first.ErrorMessage);

        var inner = new StubDispatcher(_ => throw new InvalidOperationException("must not dispatch"));
        var second = await new GenerationIngestingDispatcher(inner, Ingestor()).DispatchAsync(Request(taskId, IdGen.New("req")));

        second.Success.Should().BeFalse();
        inner.Calls.Should().Be(0);
        Directory.EnumerateFiles(_env.DocumentsRoot).Should().ContainSingle();
    }

    [Fact]
    public async Task LinkInsideTheOutputRoot_IsRefused_AndNotDelivered()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        // slot 目錄換成指向 OutputRoot 之外的連結：路徑字面上在 slot 之下，實際內容在外面。
        var outside = Path.Combine(_env.Root, "outside");
        Directory.CreateDirectory(Path.Combine(outside, requestId));
        File.WriteAllText(Path.Combine(outside, requestId, "ContactsDemo-scaffold.zip"), "PK-outside");
        var bytes = File.ReadAllBytes(Path.Combine(outside, requestId, "ContactsDemo-scaffold.zip"));
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        if (!GovernedGenerationTestSupport.TryCreateDirectoryLink(Path.Combine(_env.OutputRoot, taskId), outside))
            return; // 這台機器不能建立連結（沒有 symlink 權限也沒有 junction），無法重現。

        var zipPath = $"{taskId}/{requestId}/ContactsDemo-scaffold.zip";
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, bytes.Length))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("link");
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task WorkerFailure_IsPassedThrough_WithoutIngesting()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => ExecutionResult.Fail(requestId, "{\"ok\":false,\"errors\":[]}")),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("{\"ok\":false,\"errors\":[]}");
        AssertNotDelivered(taskId, requestId);
    }

    [Fact]
    public async Task TaskWithoutAGovernedRun_IsVerifiedAndRecorded_ButNotDelivered()
    {
        // 管理員自行建立、授予同樣 grant 的任務：broker 一樣驗證並留下證據，但沒有交付對象。
        var taskId = IdGen.New("task");
        var requestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        var dispatcher = new GenerationIngestingDispatcher(
            new StubDispatcher(_ => WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size))),
            Ingestor());

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeTrue(result.ErrorMessage);
        JsonNode.Parse(result.ResultPayload!)!["delivery"]!.GetValue<string>().Should().Be("none");
        _env.Db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE document_id = @id",
                new { id = result.EvidenceRef })
            .Should().ContainSingle();
        Directory.EnumerateFiles(_env.DocumentsRoot).Should().BeEmpty();
    }

    // ── generation-worker 忙碌時：在上限內等它空出來再分派同一個請求（配額只在分派前扣一次） ──

    private static readonly TimeSpan[] NoDelays = { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero };

    private static ExecutionResult NoWorker(string requestId, string capabilityId)
        => ExecutionResult.NoWorker(requestId, $"[StrictMode] No available worker for capability '{capabilityId}'. Execution plane unavailable.");

    [Theory]
    [InlineData("generation.catalog.query", "query_component_catalog")]
    [InlineData("generation.definition.validate", "validate_definition")]
    public async Task BusyWorker_IsWaitedFor_ForTheGenerationQueries(string capabilityId, string route)
    {
        var requestId = IdGen.New("req");
        var calls = 0;
        var inner = new StubDispatcher(_ => ++calls < 3
            ? NoWorker(requestId, capabilityId)
            : WorkerOk(requestId, "{\"ok\":true}"));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor(), NoDelays);

        var result = await dispatcher.DispatchAsync(new ApprovedRequest
        {
            RequestId = requestId,
            CapabilityId = capabilityId,
            Route = route,
            Payload = "{}",
            Scope = "{}",
            TaskId = IdGen.New("task")
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task BusyWorker_IsWaitedFor_ThenThePackageIsIngested()
    {
        var taskId = _env.SeedRun();
        var requestId = IdGen.New("req");
        var (zipPath, sha, size) = _env.WriteWorkerPackage(taskId, requestId);
        var calls = 0;
        var inner = new StubDispatcher(_ => ++calls == 1
            ? NoWorker(requestId, GenerationCapabilities.ScaffoldGenerate)
            : WorkerOk(requestId, GovernedGenerationTestSupport.WorkerPayload(taskId, requestId, zipPath, sha, size)));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor(), NoDelays);

        var result = await dispatcher.DispatchAsync(Request(taskId, requestId));

        result.Success.Should().BeTrue(result.ErrorMessage);
        inner.Calls.Should().Be(2);
        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Ingested);
    }

    [Fact]
    public async Task BusyWorker_IsWaitedForOnlyUpToTheLimit()
    {
        var requestId = IdGen.New("req");
        var inner = new StubDispatcher(_ => NoWorker(requestId, GenerationCapabilities.DefinitionValidate));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor(), NoDelays);

        var result = await dispatcher.DispatchAsync(new ApprovedRequest
        {
            RequestId = requestId,
            CapabilityId = GenerationCapabilities.DefinitionValidate,
            Route = GenerationCapabilities.ValidateRoute,
            Payload = "{}",
            Scope = "{}",
            TaskId = IdGen.New("task")
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No available worker");
        inner.Calls.Should().Be(NoDelays.Length + 1);
    }

    [Theory]
    [InlineData("All worker dispatch attempts failed")]
    [InlineData("No available worker for capability 'generation.definition.validate' and route 'validate_definition' is not supported by fallback dispatcher.")]
    public async Task ATimedOutDispatch_IsNotRetriedAsBusy(string message)
    {
        // 分派後逾時（請求已送到 worker）的結果沒有 NoWorkerAvailable：即使訊息看起來像「沒有可用的 worker」，
        // 也不再分派一次。先前以訊息判斷時，非 strict 模式下一個請求最多會送出 15 次。
        var requestId = IdGen.New("req");
        var inner = new StubDispatcher(_ => ExecutionResult.Fail(requestId, message));
        var dispatcher = new GenerationIngestingDispatcher(inner, Ingestor(), NoDelays);

        var result = await dispatcher.DispatchAsync(new ApprovedRequest
        {
            RequestId = requestId,
            CapabilityId = GenerationCapabilities.DefinitionValidate,
            Route = GenerationCapabilities.ValidateRoute,
            Payload = "{}",
            Scope = "{}",
            TaskId = IdGen.New("task")
        });

        result.Success.Should().BeFalse();
        inner.Calls.Should().Be(1, "a request that reached the worker is not dispatched again");
    }

    [Fact]
    public async Task WorkerRefusalsAndOtherRoutes_AreNotRetried()
    {
        var requestId = IdGen.New("req");
        var refusing = new StubDispatcher(_ => new ExecutionResult
        {
            RequestId = requestId,
            Success = false,
            ErrorMessage = "No available worker in this answer, but the worker itself refused",
            AnsweredByWorker = true
        });
        var validate = new ApprovedRequest
        {
            RequestId = requestId,
            CapabilityId = GenerationCapabilities.DefinitionValidate,
            Route = GenerationCapabilities.ValidateRoute,
            Payload = "{}",
            Scope = "{}",
            TaskId = IdGen.New("task")
        };
        (await new GenerationIngestingDispatcher(refusing, Ingestor(), NoDelays).DispatchAsync(validate)).Success.Should().BeFalse();
        refusing.Calls.Should().Be(1, "a worker's own refusal is final");

        var other = new StubDispatcher(_ => NoWorker(requestId, "file.read"));
        var read = new ApprovedRequest { RequestId = requestId, CapabilityId = "file.read", Route = "read_file", Payload = "{}", Scope = "{}", TaskId = IdGen.New("task") };
        (await new GenerationIngestingDispatcher(other, Ingestor(), NoDelays).DispatchAsync(read)).Success.Should().BeFalse();
        other.Calls.Should().Be(1, "other capabilities keep the function pool's fail-fast behaviour");
    }
}
