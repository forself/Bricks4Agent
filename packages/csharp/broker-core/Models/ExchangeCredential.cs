using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 用戶自己的交易所 API 憑證。每個 (owner_principal_id, exchange) 一筆——同一交易所
/// 同一用戶只能有一組 active key（如要切 demo / live、用 IsDemo 旗標明示）。
///
/// API key / secret 用 master key AES-256-GCM 加密、AAD 綁 entry_id + exchange、
/// 改任一欄解密會失敗。Master key 走 secret file mount、丟了 broker 跑不起來、
/// 等於整批 credential 也無法解—— 這是 trade-off：可用性 vs 安全。
///
/// last_used_at 給 UI / 稽核看「這把 key 還有沒有在用」。
/// </summary>
[Table("exchange_credentials")]
public class ExchangeCredential
{
    [Key(AutoIncrement = false)]
    [Column("entry_id")]
    [MaxLength(64)]
    public string EntryId { get; set; } = string.Empty;     // {owner}:{exchange}:{counter}

    [Column("owner_principal_id")]
    [Required]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = string.Empty;

    [Column("exchange")]
    [Required]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;     // bingx / binance / alpaca

    [Column("label")]
    [MaxLength(80)]
    public string? Label { get; set; }                        // 自己取的辨識名（"主帳號" / "Demo" 等）

    [Column("api_key_enc")]
    [Required]
    public string ApiKeyEnc { get; set; } = string.Empty;     // base64(nonce|ct|tag)

    [Column("api_secret_enc")]
    [Required]
    public string ApiSecretEnc { get; set; } = string.Empty;

    [Column("is_demo")]
    public bool IsDemo { get; set; }                          // BingX VST / Alpaca paper / Binance testnet

    [Column("disabled")]
    public bool Disabled { get; set; }                        // user 暫時停用、保留 key 不刪

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
