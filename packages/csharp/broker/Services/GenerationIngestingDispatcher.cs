using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrokerCore;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// 執行分派的裝飾器：只處理受治理生成的 generate（route <c>generate_scaffold</c>），其他請求原樣交給內層分派器。
///
/// generate 由 worker 回報成功後，broker 不直接採信它的結果，而是交給 <see cref="GenerationPackageIngestor"/>
/// 驗證並收下產物；驗證不通過時這次執行記為失敗、不交付。
/// </summary>
public sealed class GenerationIngestingDispatcher : IExecutionDispatcher
{
    private readonly IExecutionDispatcher _inner;
    private readonly GenerationPackageIngestor _ingestor;

    public GenerationIngestingDispatcher(IExecutionDispatcher inner, GenerationPackageIngestor ingestor)
    {
        _inner = inner;
        _ingestor = ingestor;
    }

    public static bool IsGenerateRequest(ApprovedRequest request)
        => string.Equals(request.Route, GenerationCapabilities.GenerateRoute, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(request.CapabilityId, GenerationCapabilities.ScaffoldGenerate, StringComparison.OrdinalIgnoreCase);

    public async Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
    {
        if (!IsGenerateRequest(approvedRequest))
            return await _inner.DispatchAsync(approvedRequest);

        var refusal = _ingestor.CheckBeforeDispatch(approvedRequest);
        if (refusal != null)
            return ExecutionResult.Fail(approvedRequest.RequestId, refusal);

        var result = await _inner.DispatchAsync(approvedRequest);
        if (!result.Success)
            return result;

        return await _ingestor.IngestAsync(approvedRequest, result);
    }
}

/// <summary>
/// 收下 generation-worker 產出的 zip（設計 §2 第 7 步）：
/// 1. worker 回傳的 output_slot、request_id 必須等於這次請求 grant scope 的 slot 與請求 id；
/// 2. zip.path 必須是 <c>{slot}/{requestId}/{name}-scaffold.zip</c> 形式的相對路徑（不得有 <c>..</c>、絕對路徑或反斜線），
///    解析後仍在 broker 端 <c>Generation:OutputRoot</c> 之下，且路徑上沒有符號連結或 junction；
/// 3. 重新計算 sha256 與大小並比對；
/// 4. 複製到使用者的文件區（複製後再算一次 sha256），寫入證據文件 <c>generation.execution.{requestId}</c>，
///    更新執行紀錄為 ingested 並通知交付服務。
/// 任何一步失敗就回傳失敗、不交付；任務沒有受治理生成紀錄（例如管理員自行建立的任務）時只驗證與留證據，不交付。
/// </summary>
public sealed class GenerationPackageIngestor
{
    public const string Author = "system:generation-ingestor";

    private static readonly Regex SafeSegment = new("^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant);
    private static readonly Regex ZipFileName = new("^[A-Za-z0-9_-]{1,80}-scaffold\\.zip$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    private readonly GovernedGenerationOptions _options;
    private readonly GovernedGenerationRunStore _runs;
    private readonly HighLevelLineWorkspaceService _workspace;
    private readonly BrokerDb _db;
    private readonly GenerationDeliverySignal _signal;
    private readonly ILogger<GenerationPackageIngestor> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GenerationPackageIngestor(
        GovernedGenerationOptions options,
        GovernedGenerationRunStore runs,
        HighLevelLineWorkspaceService workspace,
        BrokerDb db,
        GenerationDeliverySignal signal,
        ILogger<GenerationPackageIngestor> logger)
    {
        _options = options;
        _runs = runs;
        _workspace = workspace;
        _db = db;
        _signal = signal;
        _logger = logger;
    }

    /// <summary>
    /// 送到 worker 之前的檢查：grant scope 必須帶格式正確的 output_slot；
    /// 任務有受治理生成紀錄時，slot 必須是該任務，且紀錄仍在等待產物（已交付或已失敗的任務不再生成）。
    /// </summary>
    public string? CheckBeforeDispatch(ApprovedRequest request)
    {
        if (!TryReadScopeSlot(request.Scope, out var slot))
            return "Generation grant scope has no valid output_slot.";

        var run = _runs.Get(request.TaskId);
        if (run == null)
            return null;

        if (!string.Equals(slot, run.OutputSlot, StringComparison.Ordinal))
            return "Generation grant scope does not belong to this task.";

        return run.Status is GovernedGenerationRunStatus.Running or GovernedGenerationRunStatus.Launching
            ? null
            : "A package was already produced for this task, or the task has ended.";
    }

    public async Task<ExecutionResult> IngestAsync(ApprovedRequest request, ExecutionResult workerResult)
    {
        await _gate.WaitAsync();
        try
        {
            return Ingest(request, workerResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generated package for request {RequestId} could not be ingested.", request.RequestId);
            return Refuse(request, "the package could not be verified");
        }
        finally
        {
            _gate.Release();
        }
    }

    private ExecutionResult Ingest(ApprovedRequest request, ExecutionResult workerResult)
    {
        var outputRoot = _options.ResolveOutputRoot();
        if (outputRoot == null || !Directory.Exists(outputRoot))
            return Refuse(request, "the generation output root is not configured");

        if (!TryReadScopeSlot(request.Scope, out var slot))
            return Refuse(request, "the grant scope has no valid output_slot");

        JsonObject payload;
        try
        {
            payload = JsonNode.Parse(workerResult.ResultPayload ?? string.Empty) as JsonObject
                ?? throw new JsonException("not an object");
        }
        catch (JsonException)
        {
            return Refuse(request, "the worker result is not a JSON object");
        }

        if (!ReadString(payload, "output_slot", out var reportedSlot) || !string.Equals(reportedSlot, slot, StringComparison.Ordinal))
            return Refuse(request, "the reported output slot does not match the grant scope");

        if (!ReadString(payload, "request_id", out var reportedRequestId) || !string.Equals(reportedRequestId, request.RequestId, StringComparison.Ordinal))
            return Refuse(request, "the reported request id does not match");

        if (payload["zip"] is not JsonObject zip ||
            !ReadString(zip, "path", out var zipPath) ||
            !ReadString(zip, "sha256", out var reportedSha) || !Sha256Hex.IsMatch(reportedSha) ||
            zip["size"] is not JsonValue sizeNode || !sizeNode.TryGetValue<long>(out var reportedSize) || reportedSize <= 0)
        {
            return Refuse(request, "the reported zip entry is incomplete");
        }

        if (!TryResolvePackage(outputRoot, slot, request.RequestId, zipPath, out var packagePath, out var pathError))
            return Refuse(request, pathError);

        var info = new FileInfo(packagePath);
        if (!info.Exists)
            return Refuse(request, "the package file is missing");
        if (info.Length != reportedSize || info.Length > _options.MaxPackageBytes)
            return Refuse(request, "the package size does not match");

        var actualSha = HashFile(packagePath);
        if (!string.Equals(actualSha, reportedSha, StringComparison.OrdinalIgnoreCase))
            return Refuse(request, "the package sha256 does not match");

        var run = _runs.Get(request.TaskId);
        if (run != null && run.Status is not (GovernedGenerationRunStatus.Running or GovernedGenerationRunStatus.Launching))
            return Refuse(request, "a package was already produced for this task, or the task has ended");

        string deliveredFileName = string.Empty;
        string deliveredFilePath = string.Empty;
        if (run != null)
        {
            if (!TryCopyToUserDocuments(run, packagePath, actualSha, out deliveredFilePath, out deliveredFileName, out var copyError))
                return Refuse(request, copyError);
        }

        var evidenceId = GovernedGenerationRunStore.BuildEvidenceDocumentId(request.RequestId);
        WriteEvidence(evidenceId, request, payload, zipPath, actualSha, info.Length, deliveredFileName, run != null);

        if (run != null)
        {
            var pages = payload["pages"] as JsonArray;
            var fileCount = payload["file_count"] is JsonValue countNode && countNode.TryGetValue<int>(out var count) ? count : 0;
            var updated = _runs.TryUpdate(request.TaskId, Author, current =>
            {
                if (current.Status is not (GovernedGenerationRunStatus.Running or GovernedGenerationRunStatus.Launching))
                    return false;
                current.Status = GovernedGenerationRunStatus.Ingested;
                current.RequestId = request.RequestId;
                current.ZipSha256 = actualSha;
                current.ZipSize = info.Length;
                current.PageCount = pages?.Count ?? 0;
                current.FileCount = fileCount;
                current.DeliveredFileName = deliveredFileName;
                current.DeliveredFilePath = deliveredFilePath;
                current.EvidenceDocumentId = evidenceId;
                return true;
            });

            if (!updated)
            {
                TryDelete(deliveredFilePath);
                return Refuse(request, "the task ended while the package was being verified");
            }

            _signal.Notify();
        }

        _logger.LogInformation(
            "Generated package accepted: task={TaskId} request={RequestId} size={Size} delivery={Delivery}",
            request.TaskId, request.RequestId, info.Length, run != null ? "queued" : "none");

        var result = new JsonObject
        {
            ["ok"] = true,
            ["output_slot"] = slot,
            ["request_id"] = request.RequestId,
            ["zip"] = new JsonObject
            {
                ["path"] = zipPath,
                ["sha256"] = actualSha,
                ["size"] = info.Length
            },
            ["pages"] = payload["pages"]?.DeepClone(),
            ["file_count"] = payload["file_count"]?.DeepClone(),
            ["validation_digest"] = payload["validation_digest"]?.DeepClone(),
            ["generator_version"] = payload["generator_version"]?.DeepClone(),
            ["catalog_sha256"] = payload["catalog_sha256"]?.DeepClone(),
            ["evidence_ref"] = evidenceId,
            ["delivery"] = run != null ? "queued" : "none"
        };

        var ok = ExecutionResult.Ok(request.RequestId, result.ToJsonString(), evidenceId);
        ok.AnsweredByWorker = workerResult.AnsweredByWorker;
        return ok;
    }

    /// <summary>
    /// zip.path 必須恰好是 <c>{slot}/{requestId}/{name}-scaffold.zip</c>，解析後在 OutputRoot 之下，
    /// 且 slot 目錄、請求目錄與 zip 本身都不是符號連結或 junction。
    /// </summary>
    internal static bool TryResolvePackage(
        string outputRoot,
        string slot,
        string requestId,
        string zipPath,
        out string packagePath,
        out string error)
    {
        packagePath = string.Empty;
        error = string.Empty;

        if (zipPath.Length == 0 || zipPath.Contains('\\') || zipPath.Contains(':') || zipPath.Contains('\0') ||
            zipPath.StartsWith('/') || Path.IsPathRooted(zipPath))
        {
            error = "the zip path is not a relative path";
            return false;
        }

        var segments = zipPath.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            error = "the zip path is not a plain relative path";
            return false;
        }

        if (segments.Length != 3 ||
            !string.Equals(segments[0], slot, StringComparison.Ordinal) ||
            !string.Equals(segments[1], requestId, StringComparison.Ordinal) ||
            !SafeSegment.IsMatch(segments[0]) || !SafeSegment.IsMatch(segments[1]) ||
            !ZipFileName.IsMatch(segments[2]))
        {
            error = "the zip path is not under this request's output slot";
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var slotDirectory = Path.Combine(root, segments[0]);
        var requestDirectory = Path.Combine(slotDirectory, segments[1]);
        var candidate = Path.GetFullPath(Path.Combine(requestDirectory, segments[2]));
        if (!IsStrictlyInside(root, candidate))
        {
            error = "the zip path resolves outside the output root";
            return false;
        }

        foreach (var path in new[] { slotDirectory, requestDirectory, candidate })
        {
            if (IsLink(path))
            {
                error = "the zip path goes through a link";
                return false;
            }
        }

        packagePath = candidate;
        return true;
    }

    private bool TryCopyToUserDocuments(
        GovernedGenerationRun run,
        string packagePath,
        string expectedSha,
        out string deliveredPath,
        out string deliveredName,
        out string error)
    {
        deliveredPath = string.Empty;
        deliveredName = string.Empty;
        error = string.Empty;

        if (!string.Equals(run.Channel, "line", StringComparison.OrdinalIgnoreCase))
        {
            error = "the task's channel has no delivery target";
            return false;
        }

        var paths = _workspace.GetManagedPaths(run.UserId, ensureExists: true);
        if (paths == null || string.IsNullOrWhiteSpace(paths.DocumentsRoot))
        {
            error = "the user's workspace was not found";
            return false;
        }

        Directory.CreateDirectory(paths.DocumentsRoot);
        if (IsLink(paths.DocumentsRoot))
        {
            error = "the user's documents folder is a link";
            return false;
        }

        var baseName = SanitizeDeliveryName(string.IsNullOrWhiteSpace(run.ProjectFolderName) ? run.PackageName : run.ProjectFolderName);
        var fileName = $"{baseName}-scaffold.zip";
        var target = Path.Combine(paths.DocumentsRoot, fileName);
        if (File.Exists(target) || Directory.Exists(target))
        {
            fileName = $"{baseName}-scaffold-{ShortTaskSuffix(run.TaskId)}.zip";
            target = Path.Combine(paths.DocumentsRoot, fileName);
        }

        var temporary = Path.Combine(paths.DocumentsRoot, $".{Guid.NewGuid():N}.partial");
        try
        {
            using (var source = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
            }

            // 複製之後再算一次：交付出去的檔案就是驗證過的內容。
            if (!string.Equals(HashFile(temporary), expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                error = "the copied package does not match the verified sha256";
                return false;
            }

            File.Move(temporary, target, overwrite: false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Generated package for task {TaskId} could not be copied to the user's documents.", run.TaskId);
            error = "the package could not be copied to the user's documents";
            return false;
        }
        finally
        {
            TryDelete(temporary);
        }

        deliveredPath = target;
        deliveredName = fileName;
        return true;
    }

    private void WriteEvidence(
        string evidenceId,
        ApprovedRequest request,
        JsonObject payload,
        string zipPath,
        string sha256,
        long size,
        string deliveredFileName,
        bool delivered)
    {
        var evidence = new JsonObject
        {
            ["kind"] = "generation.execution",
            ["request_id"] = request.RequestId,
            ["task_id"] = request.TaskId,
            ["capability_id"] = request.CapabilityId,
            ["output_slot"] = payload["output_slot"]?.DeepClone(),
            ["zip"] = new JsonObject
            {
                ["path"] = zipPath,
                ["sha256"] = sha256,
                ["size"] = size
            },
            ["pages"] = payload["pages"]?.DeepClone(),
            ["file_count"] = payload["file_count"]?.DeepClone(),
            ["validation_digest"] = payload["validation_digest"]?.DeepClone(),
            ["generator_version"] = payload["generator_version"]?.DeepClone(),
            ["catalog_sha256"] = payload["catalog_sha256"]?.DeepClone(),
            ["delivered_file_name"] = deliveredFileName,
            ["delivery"] = delivered ? "queued" : "none",
            ["verified_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var latestVersion = _db.Scalar<int?>(
            "SELECT MAX(version) FROM shared_context_entries WHERE document_id = @docId",
            new { docId = evidenceId });

        _db.Insert(new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = evidenceId,
            Version = (latestVersion ?? 0) + 1,
            ParentVersion = latestVersion,
            Key = evidenceId,
            ContentRef = evidence.ToJsonString(),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"" + Author + "\"]}",
            AuthorPrincipalId = Author,
            TaskId = request.TaskId,
            Tags = "[\"governed-generation\",\"evidence\"]",
            CreatedAt = DateTime.UtcNow
        });
    }

    private ExecutionResult Refuse(ApprovedRequest request, string reason)
    {
        _logger.LogWarning("Generated package for request {RequestId} was refused: {Reason}", request.RequestId, reason);
        return ExecutionResult.Fail(request.RequestId, $"Generated package was not accepted: {reason}.");
    }

    internal static bool TryReadScopeSlot(string? scope, out string slot)
    {
        slot = string.Empty;
        try
        {
            if (JsonNode.Parse(scope ?? string.Empty) is not JsonObject root ||
                !ReadString(root, "output_slot", out var value) ||
                !SafeSegment.IsMatch(value))
            {
                return false;
            }

            slot = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ReadString(JsonObject node, string name, out string value)
    {
        value = string.Empty;
        if (node[name] is not JsonValue jsonValue || jsonValue.GetValueKind() != JsonValueKind.String)
            return false;
        value = jsonValue.GetValue<string>();
        return true;
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SanitizeDeliveryName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(invalid, '-');
        trimmed = trimmed.Trim('.', ' ');
        if (trimmed.Length > 80)
            trimmed = trimmed[..80].Trim('.', ' ');
        return trimmed.Length == 0 ? "system" : trimmed;
    }

    private static string ShortTaskSuffix(string taskId)
    {
        var parts = taskId.Split('_');
        var tail = parts.Length > 0 ? parts[^1] : taskId;
        return tail.Length > 8 ? tail[..8].ToLowerInvariant() : tail.ToLowerInvariant();
    }

    private static bool IsStrictlyInside(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison) && candidate.Length > prefix.Length;
    }

    private static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists && (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget != null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 暫存檔留下不影響結果；下次清理工作區時移除。
        }
    }
}

/// <summary>ingest 收下產物後通知交付服務立即處理（不必等下一次輪詢）。</summary>
public sealed class GenerationDeliverySignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有一個尚未處理的通知。
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _signal.WaitAsync(timeout, cancellationToken);
}
