using System.Text.Json;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>受治理生成一次執行（一個任務）的狀態。</summary>
public static class GovernedGenerationRunStatus
{
    /// <summary>已建立任務，正在啟動代理容器。</summary>
    public const string Launching = "launching";

    /// <summary>代理已啟動，等待它產出並送出生成請求。</summary>
    public const string Running = "running";

    /// <summary>產物已通過 broker 驗證並複製到使用者的文件區，等待交付。</summary>
    public const string Ingested = "ingested";

    /// <summary>已交付（產物紀錄、通知），任務完成。</summary>
    public const string Delivered = "delivered";

    /// <summary>失敗（啟動失敗、逾時、代理未產出、交付失敗、管理員停止）。</summary>
    public const string Failed = "failed";

    public static bool IsOpen(string? status)
        => status is Launching or Running or Ingested;
}

/// <summary>
/// 受治理生成一次執行的紀錄（系統文件 <c>generation.run.{taskId}</c>，global 範圍，只有管理員能經 context API 讀取）。
/// 內含 broker 內部的檔案位置，不交給代理。
/// </summary>
public sealed class GovernedGenerationRun
{
    public string TaskId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectFolderName { get; set; } = string.Empty;

    /// <summary>generate 的 grant scope 中的 package_name（淨化後的英數名稱）。</summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>generate 的 grant scope 中的 output_slot（等於任務 id）。</summary>
    public string OutputSlot { get; set; } = string.Empty;

    public string PrincipalId { get; set; } = string.Empty;
    public string AgentWorkerId { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string Status { get; set; } = GovernedGenerationRunStatus.Launching;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>被採用的 generate 執行請求。</summary>
    public string RequestId { get; set; } = string.Empty;
    public string ZipSha256 { get; set; } = string.Empty;
    public long ZipSize { get; set; }
    public int PageCount { get; set; }
    public int FileCount { get; set; }

    /// <summary>複製到使用者文件區後的檔名與位置（broker 內部使用，不出現在回覆）。</summary>
    public string DeliveredFileName { get; set; } = string.Empty;
    public string DeliveredFilePath { get; set; } = string.Empty;

    public string EvidenceDocumentId { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>交付時丟出例外的次數（ingested 之後）；達到上限即標為失敗。</summary>
    public int DeliveryAttempts { get; set; }
}

/// <summary>
/// 受治理生成執行紀錄的存取。每次狀態變更寫入新版本（shared-context 的版本鏈），只採信系統元件在 global 範圍寫入的版本。
/// 狀態轉換以 <see cref="TryUpdate"/> 在同一把鎖內「讀最新版本 → 檢查 → 寫入」，ingest 與交付服務不會互相覆寫。
/// </summary>
public sealed class GovernedGenerationRunStore
{
    public const string DocumentPrefix = "generation.run.";
    public const string ExecutionEvidencePrefix = "generation.execution.";

    private static readonly object Gate = new();
    private readonly BrokerDb _db;

    public GovernedGenerationRunStore(BrokerDb db)
    {
        _db = db;
    }

    public static string BuildDocumentId(string taskId) => DocumentPrefix + taskId;

    public static string BuildEvidenceDocumentId(string requestId) => ExecutionEvidencePrefix + requestId;

    public GovernedGenerationRun? Get(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return null;

        var json = _db.Scalar<string>(
            $"SELECT content_ref FROM shared_context_entries WHERE document_id = @docId AND {SystemContextDocuments.TrustedGlobalCondition()} ORDER BY version DESC LIMIT 1",
            new { docId = BuildDocumentId(taskId) });
        return Deserialize(json);
    }

    /// <summary>寫入一筆新的執行紀錄（同一任務已有紀錄時丟出例外）。</summary>
    public void Create(GovernedGenerationRun run, string author)
    {
        lock (Gate)
        {
            if (Get(run.TaskId) != null)
                throw new InvalidOperationException("A generation run already exists for this task.");

            run.UpdatedAt = DateTimeOffset.UtcNow;
            Append(run, author);
        }
    }

    /// <summary>
    /// 讀取最新版本並交給 <paramref name="mutate"/>；它回傳 true 時寫入新版本。
    /// 紀錄不存在或 <paramref name="mutate"/> 回傳 false 時不寫入，並回傳 false。
    /// </summary>
    public bool TryUpdate(string taskId, string author, Func<GovernedGenerationRun, bool> mutate)
        => TryUpdate(taskId, author, mutate, out _);

    public bool TryUpdate(string taskId, string author, Func<GovernedGenerationRun, bool> mutate, out GovernedGenerationRun? updated)
    {
        lock (Gate)
        {
            updated = Get(taskId);
            if (updated == null || !mutate(updated))
                return false;

            updated.UpdatedAt = DateTimeOffset.UtcNow;
            Append(updated, author);
            return true;
        }
    }

    /// <summary>仍在進行中（啟動中、執行中、待交付）的執行。</summary>
    public IReadOnlyList<GovernedGenerationRun> ListOpen()
    {
        var rows = _db.Query<SharedContextEntry>(
            BuildListOpenSql(_db.Db.DatabaseType),
            new
            {
                prefix = DocumentPrefix + "%",
                lower = DocumentPrefix,
                upper = DocumentPrefixUpperBound,
                delivered = StatusPattern(GovernedGenerationRunStatus.Delivered),
                failed = StatusPattern(GovernedGenerationRunStatus.Failed)
            });

        return rows
            .Select(row => Deserialize(row.ContentRef))
            .Where(run => run != null && GovernedGenerationRunStatus.IsOpen(run.Status))
            .Select(run => run!)
            .OrderBy(run => run.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// 前綴的上界：把 <see cref="DocumentPrefix"/> 最後的 '.' 換成下一個字元 '/'。
    /// 依位元組比較時，<c>document_id &gt;= 前綴 AND document_id &lt; 上界</c> 恰好是以前綴開頭的所有 id。
    /// </summary>
    internal const string DocumentPrefixUpperBound = "generation.run/";

    /// <summary>
    /// <see cref="ListOpen"/> 的查詢。
    /// SQLite 的 LIKE 不分大小寫，用不到 BINARY 定序的 (document_id, version) 索引，會掃過整張 shared_context_entries
    /// （這張表也存放對話紀錄與其他系統文件，會持續變大）；所以 SQLite 改用範圍條件。其他資料庫的定序不一定依位元組排序，
    /// 範圍條件可能漏掉文件，沿用前綴 LIKE（這些資料庫的前綴 LIKE 可以走索引）。
    /// 最新版本已結束（delivered、failed）的執行在查詢中先排除，不必逐筆反序列化；是否進行中仍以反序列化後的狀態為準。
    /// </summary>
    internal static string BuildListOpenSql(BaseOrm.DbType databaseType)
    {
        var trusted = SystemContextDocuments.TrustedGlobalCondition("e");
        var trustedLatest = SystemContextDocuments.TrustedGlobalCondition("x");
        var prefixCondition = databaseType == BaseOrm.DbType.SQLite
            ? "e.document_id >= @lower AND e.document_id < @upper"
            : "e.document_id LIKE @prefix";
        return $@"SELECT e.* FROM shared_context_entries e
               WHERE {prefixCondition} AND {trusted}
                 AND e.content_ref NOT LIKE @delivered AND e.content_ref NOT LIKE @failed
                 AND e.version = (SELECT MAX(x.version) FROM shared_context_entries x
                                  WHERE x.document_id = e.document_id AND {trustedLatest})";
    }

    /// <summary>執行紀錄 JSON 中某個狀態的片段（與 <see cref="Append"/> 的序列化方式相同），供查詢先排除已結束的執行。</summary>
    internal static string StatusPattern(string status)
    {
        var fragment = JsonSerializer.Serialize(new Dictionary<string, string> { [nameof(GovernedGenerationRun.Status)] = status });
        return "%" + fragment[1..^1] + "%";
    }

    private void Append(GovernedGenerationRun run, string author)
    {
        var documentId = BuildDocumentId(run.TaskId);
        var latestVersion = _db.Scalar<int?>(
            "SELECT MAX(version) FROM shared_context_entries WHERE document_id = @docId",
            new { docId = documentId });

        _db.Insert(new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latestVersion ?? 0) + 1,
            ParentVersion = latestVersion,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(run),
            ContentType = "application/json",
            Acl = "{\"read\":[\"system:generation\"],\"write\":[\"system:generation\"]}",
            AuthorPrincipalId = author,
            TaskId = SystemContextDocuments.GlobalTaskId,
            Tags = "[\"governed-generation\"]",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static GovernedGenerationRun? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GovernedGenerationRun>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
