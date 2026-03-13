using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 共享上下文服務 —— Plan-Related Context 的 CRUD + ACL 強制
///
/// 語意邊界：僅承接 plan docs / node output / evidence refs / handoff docs。
/// 不是泛用 KV 儲存。
/// </summary>
public interface ISharedContextService
{
    /// <summary>寫入 plan-related context（新建或新版本）</summary>
    SharedContextEntry Write(string authorPrincipalId, string documentId, string key,
                             string contentRef, string contentType, string acl, string? taskId);

    /// <summary>讀取最新版本（ACL 強制檢查）</summary>
    SharedContextEntry? ReadLatest(string documentId, string readerPrincipalId);

    /// <summary>按 Key + TaskId 讀取最新版本（node output 查詢）</summary>
    SharedContextEntry? ReadByKey(string key, string? taskId, string readerPrincipalId);

    /// <summary>列出文件版本歷史</summary>
    List<SharedContextEntry> ListVersions(string documentId, string readerPrincipalId);

    /// <summary>列出 task 下所有 context entries（最新版本）</summary>
    List<SharedContextEntry> ListByTask(string taskId, string readerPrincipalId);
}
