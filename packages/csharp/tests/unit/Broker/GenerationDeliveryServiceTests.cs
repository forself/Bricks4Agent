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
/// - 期限內且代理仍在執行時不動它。
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

        public Task<GeneratedPackageDeliveryResult> DeliverAsync(GovernedGenerationRun run, CancellationToken cancellationToken)
        {
            Delivered.Add(run);
            return Task.FromResult(new GeneratedPackageDeliveryResult
            {
                Success = Succeed,
                ArtifactId = Succeed ? "artifact_test" : string.Empty,
                Message = Succeed ? "ok" : "failed"
            });
        }
    }

    private GenerationDeliveryService Service() => new(
        _env.Options,
        _env.Runs,
        _delivery,
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
}
