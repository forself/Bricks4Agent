using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 共享上下文 —— 文件模型（版本化，取代 P2P 通訊）
/// Agent 無狀態原則：所有記憶/上下文持久化於此
/// </summary>
[Table("shared_context_entries")]
public class SharedContextEntry
{
    [Key(AutoIncrement = false)]
    [Column("entry_id")]
    public string EntryId { get; set; } = string.Empty;

    /// <summary>文件 ID（同一文件的多個版本共享此 ID）</summary>
    [Column("document_id")]
    [Required]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>版本號（遞增）</summary>
    [Column("version")]
    public int Version { get; set; } = 1;

    /// <summary>父版本號（null = 初始版本）</summary>
    [Column("parent_version")]
    public int? ParentVersion { get; set; }

    /// <summary>鍵名（用於 KV 式查詢）</summary>
    [Column("key")]
    [MaxLength(500)]
    public string Key { get; set; } = string.Empty;

    /// <summary>內容引用（TEXT/JSON）</summary>
    [Column("content_ref")]
    public string ContentRef { get; set; } = string.Empty;

    /// <summary>內容類型（text/plain, application/json 等）</summary>
    [Column("content_type")]
    [MaxLength(100)]
    public string ContentType { get; set; } = "application/json";

    /// <summary>存取控制清單（JSON，例如 {"read":["role_*"],"write":["role_pm"]}）</summary>
    [Column("acl")]
    public string Acl { get; set; } = "{}";

    [Column("author_principal_id")]
    [Required]
    public string AuthorPrincipalId { get; set; } = string.Empty;

    [Column("task_id")]
    public string? TaskId { get; set; }

    /// <summary>分類標籤（JSON 陣列，如 ["消費者保護法","法律"]）</summary>
    [Column("tags")]
    [MaxLength(1000)]
    public string Tags { get; set; } = "[]";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
