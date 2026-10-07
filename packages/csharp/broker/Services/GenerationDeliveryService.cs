using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Container;

namespace Broker.Services;

/// <summary>交付一個已收下的產物的結果。</summary>
public sealed class GeneratedPackageDeliveryResult
{
    public bool Success { get; init; }
    public string ArtifactId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>把已驗證並複製到使用者文件區的產物交付給使用者（產物紀錄、Drive 或簽章下載連結、LINE 通知）。</summary>
public interface IGeneratedPackageDelivery
{
    Task<GeneratedPackageDeliveryResult> DeliverAsync(GovernedGenerationRun run, CancellationToken cancellationToken);
}

/// <summary>
/// 沿用既有的交付鏈：<see cref="LineArtifactDeliveryService.DeliverExistingFileAsync"/> 記錄產物（RelatedTaskId 為該任務，
/// portal 的 /portal/artifacts 因此看得到）、上傳 Drive（失敗時改用簽章下載連結），再排入 LINE 通知。
/// </summary>
public sealed class LineGeneratedPackageDelivery : IGeneratedPackageDelivery
{
    private readonly LineArtifactDeliveryService _delivery;

    public LineGeneratedPackageDelivery(LineArtifactDeliveryService delivery)
    {
        _delivery = delivery;
    }

    public async Task<GeneratedPackageDeliveryResult> DeliverAsync(GovernedGenerationRun run, CancellationToken cancellationToken)
    {
        var uploadToGoogleDrive = _delivery.CanUploadToGoogleDrive(run.UserId, "shared_delegated");
        var result = await _delivery.DeliverExistingFileAsync(new LineExistingArtifactDeliveryRequest
        {
            UserId = run.UserId,
            FilePath = run.DeliveredFilePath,
            FileName = run.DeliveredFileName,
            UploadToGoogleDrive = uploadToGoogleDrive,
            IdentityMode = "shared_delegated",
            ShareMode = string.Empty,
            SendLineNotification = true,
            NotificationTitle = "系統雛形已生成",
            Source = "governed_generation",
            RelatedTaskType = "system_scaffold",
            RelatedDraftId = run.DraftId,
            RelatedTaskId = run.TaskId
        }, cancellationToken);

        return new GeneratedPackageDeliveryResult
        {
            Success = result.Success,
            ArtifactId = result.Artifact?.ArtifactId ?? string.Empty,
            Message = result.Message
        };
    }
}

/// <summary>
/// 受治理生成的交付與 watchdog（hosted service；ingest 收下產物時會立即喚醒，否則依間隔輪詢）。
///
/// - ingested：交付產物 → 任務 Completed → 撤銷代理的憑證與 session、停用主體 → 停止容器。
///   交付途中丟出例外（例如逾時）只算一次嘗試，留到下一輪；用完 <see cref="GovernedGenerationOptions.MaxDeliveryAttempts"/> 次才標為失敗。
/// - running／launching：超過期限，或代理已結束（有過 session、現在沒有有效的 session）卻沒有產物時，
///   停用代理、停止容器、任務標為 Failed，並以 LINE 通知使用者。
/// 單一執行的例外（包含不是由服務停止造成的取消）不會中斷這一輪，排在後面的執行照常交付與檢查。
/// 狀態轉換都經 <see cref="GovernedGenerationRunStore.TryUpdate"/>，與 ingest 互不覆寫；
/// broker 重啟後仍由執行紀錄接手（容器清單遺失時以 worker id 找不到容器就略過停止）。
/// </summary>
public sealed class GenerationDeliveryService : BackgroundService
{
    private const string Author = "system:generation-delivery";

    private readonly GovernedGenerationOptions _options;
    private readonly GovernedGenerationRunStore _runs;
    private readonly IGeneratedPackageDelivery _delivery;
    private readonly AgentSpawnService _spawnService;
    private readonly IContainerManager _containerManager;
    private readonly HighLevelLineWorkspaceService _workspace;
    private readonly BrokerDb _db;
    private readonly GenerationDeliverySignal _signal;
    private readonly ILogger<GenerationDeliveryService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _processing = new(1, 1);

    public GenerationDeliveryService(
        GovernedGenerationOptions options,
        GovernedGenerationRunStore runs,
        IGeneratedPackageDelivery delivery,
        AgentSpawnService spawnService,
        IContainerManager containerManager,
        HighLevelLineWorkspaceService workspace,
        BrokerDb db,
        GenerationDeliverySignal signal,
        ILogger<GenerationDeliveryService> logger,
        TimeProvider? timeProvider = null,
        HighLevelCoordinatorOptions? coordinatorOptions = null)
    {
        _options = options;
        _runs = runs;
        _delivery = delivery;
        _spawnService = spawnService;
        _containerManager = containerManager;
        _workspace = workspace;
        _db = db;
        _signal = signal;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        // 未提供時視為 Governed（照常輪詢）；設定值無效時 UsesGovernedPath 也是 true（fail-closed）。
        _pollWhenIdle = coordinatorOptions?.Generation.UsesGovernedPath ?? true;
    }

    private readonly bool _pollWhenIdle;

    /// <summary>
    /// Governed：依間隔輪詢（ingest 收下產物時立即喚醒）。
    /// Legacy（預設）：不會再有新的受治理生成，只在啟動時接手先前以 Governed 模式啟動、尚未結束的執行；
    /// 沒有進行中的執行時就停止輪詢，不再每隔幾秒查詢 shared_context_entries。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var waitFirst = _pollWhenIdle;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (waitFirst)
                    await _signal.WaitAsync(_options.WatchdogInterval, stoppingToken);
                waitFirst = true;

                var open = await ProcessPendingAsync(stoppingToken);
                if (!_pollWhenIdle && open == 0)
                {
                    _logger.LogInformation("System scaffold mode is Legacy and no governed generation run is open; the generation watchdog is idle.");
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Governed generation delivery pass failed.");
            }
        }
    }

    /// <summary>
    /// 處理一次所有進行中的執行（測試與 hosted loop 共用；同一時間只有一個在跑）。
    /// 回傳這一輪開始時進行中的執行數。
    /// </summary>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await _processing.WaitAsync(cancellationToken);
        try
        {
            var open = _runs.ListOpen();
            foreach (var run in open)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (run.Status == GovernedGenerationRunStatus.Ingested)
                        await DeliverAsync(run, cancellationToken);
                    else
                        await WatchAsync(run, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Governed generation run for task {TaskId} could not be processed.", run.TaskId);
                }
            }

            return open.Count;
        }
        finally
        {
            _processing.Release();
        }
    }

    private async Task DeliverAsync(GovernedGenerationRun run, CancellationToken cancellationToken)
    {
        GeneratedPackageDeliveryResult delivery;
        try
        {
            delivery = await _delivery.DeliverAsync(run, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 例外（包含逾時）只算這一次嘗試：未達上限時留在 ingested 等下一輪，達到上限才標為失敗並通知使用者。
            _logger.LogError(ex, "Generated package for task {TaskId} could not be delivered.", run.TaskId);
            var exhausted = false;
            var stillIngested = _runs.TryUpdate(run.TaskId, Author, current =>
            {
                if (current.Status != GovernedGenerationRunStatus.Ingested)
                    return false;
                current.DeliveryAttempts += 1;
                exhausted = current.DeliveryAttempts >= _options.ResolveMaxDeliveryAttempts();
                return true;
            });
            if (stillIngested && exhausted)
            {
                await FailAsync(run, "delivery_failed",
                    "系統雛形已生成，但交付給你時失敗。請聯絡管理員協助取回。", cancellationToken);
            }
            return;
        }

        if (!delivery.Success)
        {
            await FailAsync(run, "delivery_failed",
                "系統雛形已生成，但交付給你時失敗。請聯絡管理員協助取回。", cancellationToken);
            return;
        }

        var moved = _runs.TryUpdate(run.TaskId, Author, current =>
        {
            if (current.Status != GovernedGenerationRunStatus.Ingested)
                return false;
            current.Status = GovernedGenerationRunStatus.Delivered;
            current.ArtifactId = delivery.ArtifactId;
            return true;
        });
        if (!moved)
            return;

        _spawnService.DeactivateTaskAgent(run.TaskId, TaskState.Completed, "Governed generation delivered.", Author);
        await StopContainerAsync(run, cancellationToken);
        _logger.LogInformation("Governed generation delivered: task={TaskId} artifact={ArtifactId}", run.TaskId, delivery.ArtifactId);
    }

    private async Task WatchAsync(GovernedGenerationRun run, CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow() >= run.DeadlineAt)
        {
            await FailAsync(run, "deadline_exceeded",
                "系統雛形生成未在時限內完成，這次任務已結束。你可以重新送出需求再試一次。", cancellationToken);
            return;
        }

        if (run.Status == GovernedGenerationRunStatus.Running && AgentHasEnded(run))
        {
            await FailAsync(run, "agent_ended_without_package",
                "系統雛形生成沒有產出結果，這次任務已結束。你可以調整需求後重新送出。", cancellationToken);
        }
    }

    /// <summary>代理曾註冊過，而現在沒有任何有效的 session（已關閉、撤銷或過期）。</summary>
    private bool AgentHasEnded(GovernedGenerationRun run)
    {
        var sessions = _db.Query<ContainerSession>(
            "SELECT * FROM container_sessions WHERE task_id = @taskId",
            new { taskId = run.TaskId });
        if (sessions.Count == 0)
            return false;

        var now = _time.GetUtcNow().UtcDateTime;
        return sessions.All(session => session.Status != SessionStatus.Active || session.ExpiresAt <= now);
    }

    /// <summary>
    /// 管理員停止受治理生成的代理（<c>/agents/stop</c>）：仍在進行的執行標為失敗（stopped_by_admin）並通知使用者；
    /// 不論執行是否已結束，都停用代理的主體、撤銷憑證與 session，並停止它的容器。沒有這個任務的執行紀錄時回傳 false。
    /// </summary>
    public async Task<bool> StopByAdminAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var run = _runs.Get(taskId);
        if (run == null)
            return false;

        var failed = await FailAsync(run, "stopped_by_admin",
            "系統雛形生成已由管理員停止，這次任務已結束。", cancellationToken,
            current => GovernedGenerationRunStatus.IsOpen(current.Status));
        if (!failed)
        {
            _spawnService.DeactivateTaskAgent(taskId, TaskState.Failed, "Governed generation stopped by an administrator.", Author);
            await StopContainerAsync(run, cancellationToken);
        }

        return true;
    }

    private async Task<bool> FailAsync(
        GovernedGenerationRun run,
        string reason,
        string userMessage,
        CancellationToken cancellationToken,
        Func<GovernedGenerationRun, bool>? canFail = null)
    {
        // 先讀 session 再重讀紀錄：產物在這之間被收下時，紀錄已不是原狀態，這裡不會把它標為失敗。
        canFail ??= current => current.Status == run.Status;
        var failed = _runs.TryUpdate(run.TaskId, Author, current =>
        {
            if (!canFail(current))
                return false;
            current.Status = GovernedGenerationRunStatus.Failed;
            current.FailureReason = reason;
            return true;
        }, out var updated);
        if (!failed || updated == null)
            return false;

        _spawnService.DeactivateTaskAgent(run.TaskId, TaskState.Failed, $"Governed generation failed: {reason}.", Author);
        await StopContainerAsync(updated, cancellationToken);

        if (string.Equals(updated.Channel, "line", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(updated.UserId))
        {
            var project = string.IsNullOrWhiteSpace(updated.ProjectName) ? string.Empty : $"專案：{updated.ProjectName}\n";
            _workspace.QueueLineNotification(
                updated.UserId,
                "系統雛形生成未完成",
                $"{userMessage}\n\n{project}任務：{updated.TaskId}");
        }

        _logger.LogWarning("Governed generation failed: task={TaskId} reason={Reason}", run.TaskId, reason);
        return true;
    }

    private async Task StopContainerAsync(GovernedGenerationRun run, CancellationToken cancellationToken)
    {
        try
        {
            var containerId = run.ContainerId;
            if (string.IsNullOrWhiteSpace(containerId))
            {
                var managed = await _containerManager.ListManagedAsync(cancellationToken);
                containerId = managed.FirstOrDefault(container =>
                    container.WorkerType == "agent" &&
                    string.Equals(container.WorkerId, run.AgentWorkerId, StringComparison.Ordinal))?.ContainerId ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(containerId))
                await _containerManager.StopWorkerAsync(containerId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 容器可能已自行結束或 broker 重啟後不在清單中；憑證與 session 已撤銷，代理無法再呼叫 broker。
            _logger.LogWarning(ex, "Agent container for task {TaskId} could not be stopped.", run.TaskId);
        }
    }
}
