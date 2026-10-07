using System.Net;
using System.Text;
using System.Text.Json;
using Broker.Services;
using BrokerCore;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Container;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Broker;

/// <summary>
/// 受治理生成的交付與 watchdog：
/// - 已收下的產物交付後任務 Completed、代理的主體停用、憑證與 session 撤銷、容器停止；
/// - 超過期限、或代理已結束卻沒有產物時，任務 Failed、代理停用、容器停止，並以 LINE 通知使用者；
/// - 期限內且代理仍在執行時不動它；
/// - 交付逾時或丟出例外時只算一次嘗試，排在後面的執行照常處理，達到上限才標為失敗；
/// - Drive 的網路錯誤或逾時改用簽章下載連結，產物紀錄照常寫入；
/// - 管理員停止時任務 Failed、代理停用、容器停止並通知使用者。
/// </summary>
public sealed class GenerationDeliveryServiceTests : IDisposable
{
    private readonly GovernedGenerationTestSupport _env = new();
    private readonly IContainerManager _containers = Substitute.For<IContainerManager>();
    private readonly FakeDelivery _delivery = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly SessionService _sessions;
    private readonly AgentSpawnService _spawnService;

    public GenerationDeliveryServiceTests()
    {
        _sessions = new SessionService(_env.Db);
        _spawnService = new AgentSpawnService(_env.Db, new RegistrationCredentialService(_env.Db), _sessions);
        _containers.ListManagedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new List<ManagedContainer>()));
    }

    public void Dispose() => _env.Dispose();

    private sealed class FakeDelivery : IGeneratedPackageDelivery
    {
        public List<GovernedGenerationRun> Delivered { get; } = new();
        public bool Succeed { get; set; } = true;

        /// <summary>傳回非 null 時，這次交付丟出該例外。</summary>
        public Func<GovernedGenerationRun, Exception?>? ThrowFor { get; set; }

        /// <summary>交付進行中（回傳結果之前）執行的動作，例如模擬同時發生的停止。</summary>
        public Func<GovernedGenerationRun, Task>? During { get; set; }

        public async Task<GeneratedPackageDeliveryResult> DeliverAsync(GovernedGenerationRun run, CancellationToken cancellationToken)
        {
            Delivered.Add(run);
            if (During != null)
                await During(run);
            var failure = ThrowFor?.Invoke(run);
            if (failure != null)
                throw failure;
            return new GeneratedPackageDeliveryResult
            {
                Success = Succeed,
                ArtifactId = Succeed ? "artifact_test" : string.Empty,
                Message = Succeed ? "ok" : "failed"
            };
        }
    }

    private GenerationDeliveryService Service(IGeneratedPackageDelivery? delivery = null) => new(
        _env.Options,
        _env.Runs,
        delivery ?? _delivery,
        _spawnService,
        _containers,
        _env.Workspace,
        _env.Db,
        _env.Signal,
        NullLogger<GenerationDeliveryService>.Instance,
        _time);

    private BrokerTask TaskOf(string taskId) => _env.Db.Get<BrokerTask>(taskId)!;

    private Principal Principal(string taskId) => _env.Db.Get<Principal>(TaskOf(taskId).AssignedPrincipalId!)!;

    private ContainerSession OpenSession(string taskId)
        => _sessions.RegisterSession(taskId, TaskOf(taskId).AssignedPrincipalId!, GenerationCapabilities.ExecutorRole, IdGen.New("jti"), 0, string.Empty);

    private List<HighLevelLineNotification> Notifications()
        => _env.Db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE document_id LIKE 'hlm.notify.line.%'")
            .Select(entry => JsonSerializer.Deserialize<HighLevelLineNotification>(entry.ContentRef)!)
            .ToList();

    private void Ingested(string taskId)
        => _env.Runs.TryUpdate(taskId, "system:test", run =>
        {
            run.Status = GovernedGenerationRunStatus.Ingested;
            run.DeliveredFileName = "ContactsDemo-scaffold.zip";
            run.DeliveredFilePath = Path.Combine(_env.DocumentsRoot, "ContactsDemo-scaffold.zip");
            return true;
        }).Should().BeTrue();

    [Fact]
    public async Task IngestedPackage_IsDelivered_ThenTheTaskCompletesAndTheAgentIsStopped()
    {
        var taskId = _env.SeedRun(containerId: "c0ffee000001");
        var session = OpenSession(taskId);
        Ingested(taskId);

        await Service().ProcessPendingAsync();

        _delivery.Delivered.Should().ContainSingle(run => run.TaskId == taskId);
        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Delivered);
        run.ArtifactId.Should().Be("artifact_test");
        TaskOf(taskId).State.Should().Be(TaskState.Completed);
        Principal(taskId).Status.Should().Be(EntityStatus.Disabled);
        _sessions.GetSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        await _containers.Received(1).StopWorkerAsync("c0ffee000001", Arg.Any<CancellationToken>());

        // 再處理一次不會重複交付。
        await Service().ProcessPendingAsync();
        _delivery.Delivered.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeliveryFailure_FailsTheTask_AndNotifiesTheUser()
    {
        var taskId = _env.SeedRun();
        Ingested(taskId);
        _delivery.Succeed = false;

        await Service().ProcessPendingAsync();

        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        _env.Runs.Get(taskId)!.FailureReason.Should().Be("delivery_failed");
        TaskOf(taskId).State.Should().Be(TaskState.Failed);
        Notifications().Should().ContainSingle(notification => notification.UserId == GovernedGenerationTestSupport.UserId);
    }

    [Fact]
    public async Task Deadline_FailsTheTask_StopsTheAgent_AndNotifiesTheUser()
    {
        var taskId = _env.SeedRun(containerId: "c0ffee000002");
        var session = OpenSession(taskId);
        var service = Service();

        await service.ProcessPendingAsync();
        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Running, "the agent is still working within the deadline");
        TaskOf(taskId).State.Should().Be(TaskState.Active);

        _time.Advance(_env.Options.Deadline + TimeSpan.FromSeconds(1));
        await service.ProcessPendingAsync();

        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        run.FailureReason.Should().Be("deadline_exceeded");
        TaskOf(taskId).State.Should().Be(TaskState.Failed);
        Principal(taskId).Status.Should().Be(EntityStatus.Disabled);
        _sessions.GetSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        await _containers.Received(1).StopWorkerAsync("c0ffee000002", Arg.Any<CancellationToken>());

        var notification = Notifications().Single();
        notification.UserId.Should().Be(GovernedGenerationTestSupport.UserId);
        notification.Body.Should().Contain(taskId).And.NotContain(_env.Root);
        _delivery.Delivered.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentThatEndedWithoutAPackage_IsCollectedBeforeTheDeadline()
    {
        var taskId = _env.SeedRun(containerId: string.Empty);
        var session = OpenSession(taskId);
        var service = Service();

        await service.ProcessPendingAsync();
        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Running);

        // 代理跑完、關閉 session，卻沒有送出 generate。
        _sessions.CloseSession(session.SessionId, "Agent session ending");
        _containers.ListManagedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new List<ManagedContainer>
        {
            new() { ContainerId = "c0ffee000003", WorkerType = "agent", WorkerId = _env.Runs.Get(taskId)!.AgentWorkerId }
        }));
        await service.ProcessPendingAsync();

        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        run.FailureReason.Should().Be("agent_ended_without_package");
        TaskOf(taskId).State.Should().Be(TaskState.Failed);
        await _containers.Received(1).StopWorkerAsync("c0ffee000003", Arg.Any<CancellationToken>());
        Notifications().Should().ContainSingle();
    }

    [Fact]
    public async Task AgentThatHasNotRegisteredYet_IsLeftAloneUntilTheDeadline()
    {
        var taskId = _env.SeedRun();

        await Service().ProcessPendingAsync();

        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Running);
        TaskOf(taskId).State.Should().Be(TaskState.Active);
        await _containers.DidNotReceive().StopWorkerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Notifications().Should().BeEmpty();
    }

    [Fact]
    public async Task ContainerStopFailure_DoesNotUndoTheFailure()
    {
        var taskId = _env.SeedRun(containerId: "c0ffee000004");
        _containers.StopWorkerAsync("c0ffee000004", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("runtime unavailable")));
        _time.Advance(_env.Options.Deadline + TimeSpan.FromSeconds(1));

        await Service().ProcessPendingAsync();

        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        TaskOf(taskId).State.Should().Be(TaskState.Failed);
        Principal(taskId).Status.Should().Be(EntityStatus.Disabled);
    }

    [Fact]
    public async Task DeliveryTimeout_DoesNotStopThePass_AndIsRetriedUpToTheLimit()
    {
        var stuck = _env.SeedRun(containerId: "c0ffee000005");
        Ingested(stuck);
        var late = _env.SeedRun(containerId: "c0ffee000006");
        _delivery.ThrowFor = run => run.TaskId == stuck
            ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")
            : null;
        _time.Advance(_env.Options.Deadline + TimeSpan.FromSeconds(1));
        var service = Service();

        await service.ProcessPendingAsync();

        // 排在後面的執行仍被 watchdog 處理。
        _env.Runs.Get(late)!.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        _env.Runs.Get(late)!.FailureReason.Should().Be("deadline_exceeded");
        await _containers.Received(1).StopWorkerAsync("c0ffee000006", Arg.Any<CancellationToken>());
        // 逾時的交付只算一次嘗試，留到下一輪。
        var pending = _env.Runs.Get(stuck)!;
        pending.Status.Should().Be(GovernedGenerationRunStatus.Ingested);
        pending.DeliveryAttempts.Should().Be(1);
        TaskOf(stuck).State.Should().Be(TaskState.Active);

        for (var pass = 1; pass < _env.Options.ResolveMaxDeliveryAttempts(); pass++)
            await service.ProcessPendingAsync();

        var failed = _env.Runs.Get(stuck)!;
        failed.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        failed.FailureReason.Should().Be("delivery_failed");
        failed.DeliveryAttempts.Should().Be(_env.Options.ResolveMaxDeliveryAttempts());
        _delivery.Delivered.Count(run => run.TaskId == stuck).Should().Be(_env.Options.ResolveMaxDeliveryAttempts());
        TaskOf(stuck).State.Should().Be(TaskState.Failed);
        await _containers.Received(1).StopWorkerAsync("c0ffee000005", Arg.Any<CancellationToken>());
        Notifications().Should().Contain(notification => notification.Title == "系統雛形生成未完成");

        await service.ProcessPendingAsync();
        _delivery.Delivered.Count(run => run.TaskId == stuck).Should().Be(_env.Options.ResolveMaxDeliveryAttempts(), "a failed run is not retried");
    }

    [Fact]
    public async Task ServiceShutdown_StillStopsThePass()
    {
        var taskId = _env.SeedRun();
        Ingested(taskId);
        using var stopping = new CancellationTokenSource();
        _delivery.ThrowFor = _ =>
        {
            stopping.Cancel();
            return new OperationCanceledException(stopping.Token);
        };

        var pass = () => Service().ProcessPendingAsync(stopping.Token);

        await pass.Should().ThrowAsync<OperationCanceledException>();
        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Ingested, "a shutdown is not a delivery failure");
        _env.Runs.Get(taskId)!.DeliveryAttempts.Should().Be(0);
    }

    public static TheoryData<string> DriveFailures => new() { "network", "timeout" };

    [Theory]
    [MemberData(nameof(DriveFailures))]
    public async Task DriveFailure_FallsBackToTheSignedLink_AndRecordsTheArtifact(string failure)
    {
        var drive = new DriveHarness(_env, failure);
        var taskId = _env.SeedRun();
        Ingested(taskId);
        var run = _env.Runs.Get(taskId)!;
        Directory.CreateDirectory(Path.GetDirectoryName(run.DeliveredFilePath)!);
        File.WriteAllBytes(run.DeliveredFilePath, Encoding.UTF8.GetBytes("PK-generated-package"));

        await Service(new LineGeneratedPackageDelivery(drive.Delivery)).ProcessPendingAsync();

        drive.UploadAttempts.Should().BeGreaterThan(0, "the Drive upload was attempted");
        var delivered = _env.Runs.Get(taskId)!;
        delivered.Status.Should().Be(GovernedGenerationRunStatus.Delivered);
        TaskOf(taskId).State.Should().Be(TaskState.Completed);

        var artifact = _env.Workspace.ListArtifacts(GovernedGenerationTestSupport.UserId).Single(item => item.RelatedTaskId == taskId);
        artifact.ArtifactId.Should().Be(delivered.ArtifactId);
        artifact.UploadedToGoogleDrive.Should().BeFalse();
        artifact.DeliveryMode.Should().Be("local_only");
        artifact.DriveError.Should().StartWith("google_drive_request_failed");
        artifact.OverallStatus.Should().Be("partial");

        var notification = Notifications().Single(item => item.NotificationId == artifact.NotificationId);
        notification.Title.Should().Be("系統雛形已生成");
        notification.Body.Should().Contain($"{DriveHarness.PublicBaseUrl}/api/v1/artifacts/download/").And.NotContain(_env.Root);
    }

    [Fact]
    public async Task AdminStop_FailsTheRun_StopsTheAgent_AndNotifiesTheUser()
    {
        var taskId = _env.SeedRun(containerId: "c0ffee000007");
        var session = OpenSession(taskId);

        (await Service().StopByAdminAsync(taskId)).Should().BeTrue();

        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Failed);
        run.FailureReason.Should().Be("stopped_by_admin");
        TaskOf(taskId).State.Should().Be(TaskState.Failed);
        Principal(taskId).Status.Should().Be(EntityStatus.Disabled);
        _sessions.GetSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        await _containers.Received(1).StopWorkerAsync("c0ffee000007", Arg.Any<CancellationToken>());
        Notifications().Should().ContainSingle(notification => notification.Body.Contains("管理員停止"));

        (await Service().StopByAdminAsync("task_not_a_generation_run")).Should().BeFalse();
    }

    [Fact]
    public async Task AdminStopDuringADelivery_WaitsForIt_AndOnlyStopsTheAgent()
    {
        // 交付進行中的停止要等這次交付結束：產物已送出時，執行維持 Delivered、任務 Completed，
        // 停止只停用代理與停止容器，使用者不會再收到「已由管理員停止」。
        var taskId = _env.SeedRun(containerId: "c0ffee000008");
        Ingested(taskId);
        var service = Service();
        Task<bool>? stop = null;
        var stopFinishedDuringDelivery = false;
        _delivery.During = async run =>
        {
            stop = service.StopByAdminAsync(run.TaskId);
            await Task.Delay(200);
            stopFinishedDuringDelivery = stop.IsCompleted;
        };

        await service.ProcessPendingAsync();
        (await stop!).Should().BeTrue();

        stopFinishedDuringDelivery.Should().BeFalse("the stop waits until the delivery pass is over");
        _delivery.Delivered.Should().ContainSingle();
        var run = _env.Runs.Get(taskId)!;
        run.Status.Should().Be(GovernedGenerationRunStatus.Delivered);
        TaskOf(taskId).State.Should().Be(TaskState.Completed);
        Principal(taskId).Status.Should().Be(EntityStatus.Disabled);
        await _containers.Received().StopWorkerAsync("c0ffee000008", Arg.Any<CancellationToken>());
        Notifications().Should().NotContain(notification => notification.Body.Contains("管理員停止"));
    }

    [Fact]
    public async Task ARunThatEndedAfterTheSnapshot_IsNotDelivered()
    {
        // 這一輪開始時兩個執行都在等待交付；交付第一個的期間，第二個被結束（例如其他寫入者標為失敗）。
        var first = _env.SeedRun();
        var second = _env.SeedRun();
        Ingested(first);
        Ingested(second);
        _delivery.During = run =>
        {
            var other = run.TaskId == first ? second : first;
            _env.Runs.TryUpdate(other, "system:test", current =>
            {
                current.Status = GovernedGenerationRunStatus.Failed;
                current.FailureReason = "stopped_by_admin";
                return true;
            });
            return Task.CompletedTask;
        };

        await Service().ProcessPendingAsync();

        _delivery.Delivered.Should().ContainSingle("the run that ended during the pass is read again and skipped");
        var delivered = _delivery.Delivered.Single().TaskId;
        var skipped = delivered == first ? second : first;
        _env.Runs.Get(delivered)!.Status.Should().Be(GovernedGenerationRunStatus.Delivered);
        _env.Runs.Get(skipped)!.Status.Should().Be(GovernedGenerationRunStatus.Failed);
    }

    /// <summary>
    /// 真正的交付鏈（LineArtifactDeliveryService 與 GoogleDriveShareService），Drive 的上傳以假的 HTTP handler 模擬失敗；
    /// 簽章下載連結的公開網址來自暫存的通道檔。所有值都是測試用的假值，不連外。
    /// </summary>
    private sealed class DriveHarness
    {
        public const string PublicBaseUrl = "https://sidecar.example.test";
        private int _uploads;

        public DriveHarness(GovernedGenerationTestSupport env, string failure)
        {
            var root = Path.Combine(env.Root, "drive");
            Directory.CreateDirectory(root);
            var oauthClient = Path.Combine(root, "oauth-client.json");
            File.WriteAllText(oauthClient, JsonSerializer.Serialize(new
            {
                installed = new
                {
                    client_id = "test-client",
                    project_id = "test-project",
                    auth_uri = "https://oauth.example.test/auth",
                    token_uri = "https://oauth.example.test/token",
                    client_secret = "test-client-secret"
                }
            }));
            var tunnelFile = Path.Combine(root, "last-tunnel-url");
            File.WriteAllText(tunnelFile, PublicBaseUrl);

            var driveOptions = new GoogleDriveDeliveryOptions
            {
                OAuthClientJsonPath = oauthClient,
                DefaultFolderId = "test-folder",
                DefaultIdentityMode = "shared_delegated",
                SharedDelegatedChannel = "line",
                SharedDelegatedUserId = "drive-owner"
            };
            env.Db.Insert(new GoogleDriveDelegatedCredential
            {
                CredentialId = IdGen.New("gdc"),
                Channel = "line",
                UserId = "drive-owner",
                RefreshToken = "test-refresh-token",
                Status = "active"
            });

            var tokenClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"test-access-token\"}", Encoding.UTF8, "application/json")
            }));
            var driveClient = new HttpClient(new StubHandler(_ =>
            {
                Interlocked.Increment(ref _uploads);
                throw failure == "timeout"
                    ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")
                    : new HttpRequestException("No such host is known.");
            }));
            var oauth = new GoogleDriveOAuthService(env.Db, driveOptions, tokenClient, NullLogger<GoogleDriveOAuthService>.Instance);
            var share = new GoogleDriveShareService(driveOptions, oauth, driveClient, NullLogger<GoogleDriveShareService>.Instance);
            var downloadOptions = new BrokerArtifactDownloadOptions
            {
                SigningSecret = "unit-test-signing-secret",
                SidecarLastTunnelUrlPath = tunnelFile
            };
            var downloads = new BrokerArtifactDownloadService(env.Workspace, new SidecarPublicUrlResolver(downloadOptions), downloadOptions);
            Delivery = new LineArtifactDeliveryService(env.Workspace, share, downloads, NullLogger<LineArtifactDeliveryService>.Instance);
        }

        public LineArtifactDeliveryService Delivery { get; }

        public int UploadAttempts => _uploads;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    [Fact]
    public async Task FinishedRuns_AreNotListedAgain()
    {
        var delivered = _env.SeedRun(status: GovernedGenerationRunStatus.Delivered);
        var failed = _env.SeedRun(status: GovernedGenerationRunStatus.Failed);
        _time.Advance(TimeSpan.FromDays(1));

        await Service().ProcessPendingAsync();

        _env.Runs.ListOpen().Should().BeEmpty();
        TaskOf(delivered).State.Should().Be(TaskState.Active, "a finished run is not touched again");
        TaskOf(failed).State.Should().Be(TaskState.Active);
        Notifications().Should().BeEmpty();
    }

    // ── watchdog 的查詢與 Legacy 模式 ──

    /// <summary>EXPLAIN QUERY PLAN 的一列（只取 detail）。</summary>
    public sealed class QueryPlanRow
    {
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>
    /// shared_context_entries 也存放對話紀錄與其他系統文件：watchdog 每輪的查詢在 SQLite 上必須走 (document_id, version) 索引，
    /// 不能掃整張表（SQLite 的 LIKE 不分大小寫，用不到 BINARY 定序的索引）。
    /// </summary>
    [Fact]
    public void ListOpenQuery_UsesTheDocumentIndex_OnSqlite()
    {
        var sql = GovernedGenerationRunStore.BuildListOpenSql(BaseOrm.DbType.SQLite);
        var plan = _env.Db.Query<QueryPlanRow>("EXPLAIN QUERY PLAN " + sql, new
        {
            prefix = GovernedGenerationRunStore.DocumentPrefix + "%",
            lower = GovernedGenerationRunStore.DocumentPrefix,
            upper = GovernedGenerationRunStore.DocumentPrefixUpperBound,
            delivered = GovernedGenerationRunStore.StatusPattern(GovernedGenerationRunStatus.Delivered),
            failed = GovernedGenerationRunStore.StatusPattern(GovernedGenerationRunStatus.Failed)
        }).Select(row => row.Detail).ToList();

        plan.Should().NotBeEmpty();
        plan.Should().Contain(detail => detail.Contains("idx_shared_context_doc_ver") && detail.Contains("document_id>?") && detail.Contains("document_id<?"),
            string.Join(" | ", plan));
        plan.Should().NotContain(detail => detail.StartsWith("SCAN", StringComparison.OrdinalIgnoreCase),
            "no full table scan: " + string.Join(" | ", plan));

        GovernedGenerationRunStore.BuildListOpenSql(BaseOrm.DbType.SqlServer).Should().Contain("LIKE @prefix",
            "other databases keep the prefix LIKE, because their collations need not sort by bytes");
    }

    [Fact]
    public void ListOpen_ReturnsOnlyOpenRuns_AndIgnoresDocumentsOutsideThePrefix()
    {
        GovernedGenerationRunStore.DocumentPrefixUpperBound.Should().Be(
            GovernedGenerationRunStore.DocumentPrefix[..^1] + (char)(GovernedGenerationRunStore.DocumentPrefix[^1] + 1));
        GovernedGenerationRunStore.StatusPattern(GovernedGenerationRunStatus.Failed).Should().Be("%\"Status\":\"failed\"%");

        var running = _env.SeedRun();
        var launching = _env.SeedRun(status: GovernedGenerationRunStatus.Launching);
        var ingested = _env.SeedRun();
        Ingested(ingested);
        var endedLater = _env.SeedRun();
        _env.Runs.TryUpdate(endedLater, "system:test", run =>
        {
            run.Status = GovernedGenerationRunStatus.Failed;
            return true;
        }).Should().BeTrue();
        _env.SeedRun(status: GovernedGenerationRunStatus.Delivered);

        // 前綴之外、但只差一個字元或大小寫的系統文件：不是執行紀錄，不能被當成進行中的執行。
        foreach (var lookalike in new[] { "generation.run/x", "generation.runs.x", "GENERATION.RUN.x", "generation.ru" })
        {
            _env.Db.Insert(new SharedContextEntry
            {
                EntryId = IdGen.New("ctx"),
                DocumentId = lookalike,
                Version = 1,
                Key = lookalike,
                ContentRef = "{\"TaskId\":\"task_lookalike\",\"Status\":\"running\"}",
                ContentType = "application/json",
                Acl = "{}",
                AuthorPrincipalId = "system:test",
                TaskId = SystemContextDocuments.GlobalTaskId,
                CreatedAt = DateTime.UtcNow
            });
        }

        _env.Runs.ListOpen().Select(run => run.TaskId).Should().BeEquivalentTo(new[] { running, launching, ingested });
    }

    private GenerationDeliveryService ServiceInMode(string mode) => new(
        _env.Options,
        _env.Runs,
        _delivery,
        _spawnService,
        _containers,
        _env.Workspace,
        _env.Db,
        _env.Signal,
        NullLogger<GenerationDeliveryService>.Instance,
        _time,
        new HighLevelCoordinatorOptions { Generation = new HighLevelGenerationOptions { SystemScaffoldMode = mode } });

    [Fact]
    public async Task LegacyMode_WithoutOpenRuns_StopsPolling()
    {
        var service = ServiceInMode(SystemScaffoldModes.Legacy);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var finished = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(10)));
            finished.Should().BeSameAs(service.ExecuteTask, "Legacy mode has nothing to watch and stops after the first pass");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LegacyMode_FinishesRunsLeftByGovernedMode_ThenStopsPolling()
    {
        _env.Options.WatchdogIntervalSeconds = 1;
        var taskId = _env.SeedRun(containerId: "c0ffee000009");
        Ingested(taskId);
        var service = ServiceInMode(SystemScaffoldModes.Legacy);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var finished = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(15)));
            finished.Should().BeSameAs(service.ExecuteTask, "the loop stops once no run is open");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _env.Runs.Get(taskId)!.Status.Should().Be(GovernedGenerationRunStatus.Delivered, "a run left by Governed mode is still delivered");
        _delivery.Delivered.Should().ContainSingle(run => run.TaskId == taskId);
    }

    [Fact]
    public async Task GovernedMode_KeepsPolling_WithoutOpenRuns()
    {
        var service = ServiceInMode(SystemScaffoldModes.Governed);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var finished = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(2)));
            finished.Should().NotBeSameAs(service.ExecuteTask, "Governed mode keeps watching for new runs");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
