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

        // 查詢同 documentId 的最新版本
        var existing = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId });

        var latestVersion = existing.Count > 0 ? existing[0].Version : 0;

        var entry = new SharedContextEntry
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

        _db.Insert(entry);

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

        // ACL 檢查（用最新版本的 ACL）
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

        return entries;
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
    /// </summary>
    private static bool CheckReadAccess(string acl, string readerPrincipalId)
    {
        if (string.IsNullOrEmpty(acl) || acl == "{}")
            return true; // 無 ACL = 公開

        try
        {
            using var doc = JsonDocument.Parse(acl);
            if (!doc.RootElement.TryGetProperty("read", out var readArray))
                return true; // 無 read 陣列 = 公開

            foreach (var item in readArray.EnumerateArray())
            {
                var value = item.GetString();
                if (value == null) continue;

                // "*" = 允許所有人
                if (value == "*") return true;

                // 精確匹配 principalId 或 roleId
                if (value == readerPrincipalId) return true;
            }

            return false; // 不在 read 清單中
        }
        catch
        {
            return true; // ACL 解析失敗 = 預設公開（安全降級）
        }
    }
}
