using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 向量嵌入條目 —— 儲存 memory 條目的向量表示
/// 用於語意搜尋（cosine similarity）
/// </summary>
[Table("vector_entries")]
public class VectorEntry
{
    [Key(AutoIncrement = false)]
    [Column("entry_id")]
    public string EntryId { get; set; } = string.Empty;

    /// <summary>來源 memory key（對應 SharedContextEntry.Key）</summary>
    [Column("source_key")]
    [Required]
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>任務隔離</summary>
    [Column("task_id")]
    [Required]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>嵌入的原始文字摘要（用於 debug）</summary>
    [Column("text_preview")]
    [MaxLength(500)]
    public string TextPreview { get; set; } = string.Empty;

    /// <summary>內容 SHA256 雜湊（避免重複嵌入相同內容）</summary>
    [Column("content_hash")]
    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>向量嵌入（float[] → byte[]，Little-Endian IEEE 754）</summary>
    [Column("embedding")]
    public byte[] Embedding { get; set; } = Array.Empty<byte>();

    /// <summary>嵌入模型名稱</summary>
    [Column("embedding_model")]
    [MaxLength(100)]
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>向量維度</summary>
    [Column("dimension")]
    public int Dimension { get; set; }

    /// <summary>分類標籤（JSON 陣列，如 ["消費者保護法","退貨"]）</summary>
    [Column("tags")]
    [MaxLength(1000)]
    public string Tags { get; set; } = "[]";

    /// <summary>分塊索引（0 = 完整文件，1+ = 分塊序號）</summary>
    [Column("chunk_index")]
    public int ChunkIndex { get; set; }

    /// <summary>分塊總數（0 = 未分塊）</summary>
    [Column("chunk_total")]
    public int ChunkTotal { get; set; }

    /// <summary>父文件 key（分塊時指向原始完整文件）</summary>
    [Column("parent_key")]
    [MaxLength(500)]
    public string ParentKey { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
