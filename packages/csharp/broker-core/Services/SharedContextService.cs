using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// SharedContext 實作 —— 版本化文件 + ACL 強制 + 稽核
///
/// 語意邊界：僅承接 plan-related content：
/// - application/json（node output / plan metadata）
/// - text/plain（node notes / descriptions）
/// - application/evidence（執行證據引用）
/// - application/handoff（交接文件）
/// </summary>
public class SharedContextService : ISharedContextService
{
    private readonly BrokerDb _db;
    private readonly IAuditService _auditService;

    /// <summary>允許的 content type（限 plan-related）</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "text/plain",
        "application/evidence",
        "application/handoff"
    };

    public SharedContextService(BrokerDb db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    public SharedContextEntry Write(string authorPrincipalId, string documentId, string key,
                                     string contentRef, string contentType, string acl, string? taskId)
    {
        // 驗證 content type（限 plan-related）
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException(
                $"Content type '{contentType}' not allowed. " +
                $"SharedContext only accepts plan-related types: {string.Join(", ", AllowedContentTypes)}");
        }

        // 驗證 ACL 格式：格式不符的 ACL 不寫入（讀取端也會把它視為拒絕存取）
        if (!IsWellFormedAcl(acl))
        {
            throw new InvalidOperationException(
                "ACL must be a JSON object; when it has a 'read' entry, that entry must be an array of strings.");
        }

        // 使用交易確保版本號原子遞增（修復 H-6：版本 race condition）
        var entry = _db.InTransaction(() =>
        {
            // 查詢同 documentId 的最新版本（在交易中鎖定行）
            var existing = _db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
                new { docId = documentId });

            var latestVersion = existing.Count > 0 ? existing[0].Version : 0;

            var newEntry = new SharedContextEntry
            {
                EntryId = IdGen.New("ctx"),
                DocumentId = documentId,
                Version = latestVersion + 1,
                ParentVersion = latestVersion > 0 ? latestVersion : null,
                Key = key,
                ContentRef = contentRef,
                ContentType = contentType,
                Acl = acl,
                AuthorPrincipalId = authorPrincipalId,
                TaskId = taskId,
                CreatedAt = DateTime.UtcNow
            };

            _db.Insert(newEntry);
            return newEntry;
        });

        _auditService.RecordEvent(
            traceId: entry.EntryId,
            eventType: "CONTEXT_WRITTEN",
            principalId: authorPrincipalId,
            taskId: taskId,
            details: JsonSerializer.Serialize(new
            {
                documentId,
                key,
                version = entry.Version,
                contentType
            }));

        return entry;
    }

    public SharedContextEntry? ReadLatest(string documentId, string readerPrincipalId)
    {
        var entries = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId });

        if (entries.Count == 0)
            return null;

        var entry = entries[0];

        // ACL 強制檢查
        if (!CheckReadAccess(entry.Acl, readerPrincipalId))
        {
            _auditService.RecordEvent(
                traceId: entry.EntryId,
                eventType: "CONTEXT_READ_DENIED",
                principalId: readerPrincipalId,
                details: JsonSerializer.Serialize(new { documentId, reason = "ACL denied" }));
            return null;
        }

        _auditService.RecordEvent(
            traceId: entry.EntryId,
            eventType: "CONTEXT_READ",
            principalId: readerPrincipalId,
            details: JsonSerializer.Serialize(new { documentId, version = entry.Version }));

        return entry;
    }

    public SharedContextEntry? ReadByKey(string key, string? taskId, string readerPrincipalId)
    {
        List<SharedContextEntry> entries;

        if (taskId != null)
        {
            entries = _db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE key = @key AND task_id = @tid ORDER BY version DESC LIMIT 1",
                new { key, tid = taskId });
        }
        else
        {
            entries = _db.Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE key = @key ORDER BY version DESC LIMIT 1",
                new { key });
        }

        if (entries.Count == 0)
            return null;

        var entry = entries[0];

        // ACL 強制檢查
        if (!CheckReadAccess(entry.Acl, readerPrincipalId))
        {
            _auditService.RecordEvent(
                traceId: entry.EntryId,
                eventType: "CONTEXT_READ_DENIED",
                principalId: readerPrincipalId,
                details: JsonSerializer.Serialize(new { key, taskId, reason = "ACL denied" }));
            return null;
        }

        _auditService.RecordEvent(
            traceId: entry.EntryId,
            eventType: "CONTEXT_READ",
            principalId: readerPrincipalId,
            details: JsonSerializer.Serialize(new { key, taskId, version = entry.Version }));

        return entry;
    }

    public List<SharedContextEntry> ListVersions(string documentId, string readerPrincipalId)
    {
        var entries = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version ASC",
            new { docId = documentId });

        // ACL 檢查：最新版本不可讀時整份文件不回傳；可讀時，舊版本也只回傳各自 ACL 允許讀取的版本
        if (entries.Count > 0)
        {
            var latest = entries[^1]; // 最新版本
            if (!CheckReadAccess(latest.Acl, readerPrincipalId))
            {
                _auditService.RecordEvent(
                    traceId: latest.EntryId,
                    eventType: "CONTEXT_READ_DENIED",
                    principalId: readerPrincipalId,
                    details: JsonSerializer.Serialize(new { documentId, reason = "ACL denied" }));
                return new List<SharedContextEntry>();
            }
        }

        return entries.FindAll(entry => CheckReadAccess(entry.Acl, readerPrincipalId));
    }

    public List<SharedContextEntry> ListByTask(string taskId, string readerPrincipalId)
    {
        // 取每個 document_id 的最新版本
        var entries = _db.Query<SharedContextEntry>(
            @"SELECT e.* FROM shared_context_entries e
              INNER JOIN (
                  SELECT document_id, MAX(version) AS max_ver
                  FROM shared_context_entries
                  WHERE task_id = @tid
                  GROUP BY document_id
              ) latest ON e.document_id = latest.document_id AND e.version = latest.max_ver
              WHERE e.task_id = @tid
              ORDER BY e.created_at",
            new { tid = taskId });

        // 過濾有讀取權限的 entries
        var accessible = new List<SharedContextEntry>();
        foreach (var entry in entries)
        {
            if (CheckReadAccess(entry.Acl, readerPrincipalId))
                accessible.Add(entry);
        }

        return accessible;
    }

    // ── ACL 檢查 ──

    /// <summary>
    /// 檢查 readerPrincipalId 是否在 ACL 的 read 清單中
    /// ACL 格式：{"read":["role_reader","role_admin"],"write":["role_pm"]}
    /// "*" 表示允許所有人
    ///
    /// 安全原則：Fail-Closed（解析失敗或格式不符 → 拒絕存取）
    /// </summary>
    private static bool CheckReadAccess(string acl, string readerPrincipalId)
    {
        // 無 ACL 或空 ACL → 拒絕（fail-closed）
        if (string.IsNullOrEmpty(acl) || acl == "{}")
            return false;

        try
        {
            using var doc = JsonDocument.Parse(acl);
            var root = doc.RootElement;

            // 根不是物件、read 不是字串陣列 → 拒絕（fail-closed）
            if (!IsWellFormedAcl(root))
                return false;

            if (!root.TryGetProperty("read", out var readArray))
                return false; // 無 read 陣列 → 拒絕（fail-closed）

            foreach (var item in readArray.EnumerateArray())
            {
                var value = item.GetString();

                // "*" = 允許所有人
                if (value == "*") return true;

                // 精確匹配 principalId 或 roleId
                if (value == readerPrincipalId) return true;
            }

            return false; // 不在 read 清單中
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false; // ACL 解析失敗或型別不符 → 拒絕（fail-closed）
        }
    }

    /// <summary>
    /// ACL 格式：必須是 JSON 物件；有 read 時，read 必須是字串陣列。
    /// 寫入時拒絕格式不符的 ACL；讀取時格式不符一律視為拒絕存取。
    /// </summary>
    public static bool IsWellFormedAcl(string? acl)
    {
        if (string.IsNullOrWhiteSpace(acl))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(acl);
            return IsWellFormedAcl(doc.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsWellFormedAcl(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("read", out var read))
            return true;

        if (read.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in read.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
        }

        return true;
    }
}
