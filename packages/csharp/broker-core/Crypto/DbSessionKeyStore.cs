using System.Security.Cryptography;
using BrokerCore.Data;

namespace BrokerCore.Crypto;

/// <summary>
/// DB 後端 Session 金鑰存儲（Phase 1）
///
/// 設計：
/// - session_key 用 broker 主金鑰（AES-256-GCM）加密後存入 container_sessions.encrypted_session_key
/// - 所有 broker instance 共享同一主金鑰，故可在任意節點解密
/// - last_seen_seq 用 DB 原子更新（optimistic concurrency）防並發
///
/// 叢集化：
/// - 零 in-memory-only 狀態
/// - 所有讀寫直接操作 DB
/// - Phase 2 可替換為 RedisSessionKeyStore（Redis + DB fallback）
/// </summary>
public class DbSessionKeyStore : ISessionKeyStore
{
    private readonly BrokerDb _db;
    private readonly byte[] _masterKey; // 32 bytes AES-256

    private const int NonceLength = 12;
    private const int TagLength = 16;

    /// <summary>
    /// 建構子
    /// </summary>
    /// <param name="db">BrokerDb 實例</param>
    /// <param name="masterKeyBase64">Broker 主金鑰（Base64，32 bytes AES-256）</param>
    public DbSessionKeyStore(BrokerDb db, string masterKeyBase64)
    {
        _db = db;

        if (string.IsNullOrWhiteSpace(masterKeyBase64)
            || masterKeyBase64.StartsWith("CHANGE_ME"))
        {
            // 開發模式：自動生成隨機主金鑰（每次重啟不同，session 不可跨重啟）
            _masterKey = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            _masterKey = Convert.FromBase64String(masterKeyBase64);
            if (_masterKey.Length != 32)
                throw new ArgumentException(
                    "MasterKeyBase64 must decode to exactly 32 bytes (AES-256).");
        }
    }

    /// <inheritdoc />
    public void Store(string sessionId, byte[] sessionKey)
    {
        // AES-256-GCM 加密 session_key
        var encrypted = EncryptWithMasterKey(sessionKey, sessionId);

        // 更新 DB 的 encrypted_session_key 欄位
        _db.Execute(
            "UPDATE container_sessions SET encrypted_session_key = @encrypted WHERE session_id = @sid",
            new { encrypted, sid = sessionId });
    }

    /// <inheritdoc />
    public byte[]? Retrieve(string sessionId)
    {
        var encrypted = _db.Scalar<string>(
            "SELECT encrypted_session_key FROM container_sessions WHERE session_id = @sid AND status = 0",
            new { sid = sessionId });

        if (string.IsNullOrEmpty(encrypted))
            return null;

        return DecryptWithMasterKey(encrypted, sessionId);
    }

    /// <inheritdoc />
    public void Remove(string sessionId)
    {
        // 清除 encrypted_session_key（不刪除 session 記錄，保留稽核軌跡）
        _db.Execute(
            "UPDATE container_sessions SET encrypted_session_key = '' WHERE session_id = @sid",
            new { sid = sessionId });
    }

    /// <inheritdoc />
    public bool TryAdvanceSeq(string sessionId, int newSeq)
    {
        // 原子更新：WHERE last_seen_seq < @newSeq
        // 若 newSeq <= 已見序號 → 0 rows affected → replay 攻擊
        var affected = _db.Execute(
            "UPDATE container_sessions SET last_seen_seq = @newSeq WHERE session_id = @sid AND last_seen_seq < @newSeq AND status = 0",
            new { newSeq, sid = sessionId });

        return affected > 0;
    }

    // ── 主金鑰加密/解密 ──

    /// <summary>
    /// 用主金鑰 AES-256-GCM 加密
    /// 格式：Base64(nonce + ciphertext + tag)
    /// AAD = sessionId（綁定 session，防止跨 session 替換）
    /// </summary>
    private string EncryptWithMasterKey(byte[] plaintext, string sessionId)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var aad = System.Text.Encoding.UTF8.GetBytes(sessionId);

        using var aes = new AesGcm(_masterKey, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        // nonce(12) + ciphertext(32) + tag(16) = 60 bytes
        var result = new byte[NonceLength + ciphertext.Length + TagLength];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceLength);
        Buffer.BlockCopy(ciphertext, 0, result, NonceLength, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceLength + ciphertext.Length, TagLength);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// 用主金鑰 AES-256-GCM 解密
    /// </summary>
    private byte[] DecryptWithMasterKey(string encryptedBase64, string sessionId)
    {
        var data = Convert.FromBase64String(encryptedBase64);

        if (data.Length < NonceLength + TagLength)
            throw new CryptographicException("Encrypted session key data too short.");

        var nonce = data.AsSpan(0, NonceLength);
        var ciphertextLength = data.Length - NonceLength - TagLength;
        var ciphertext = data.AsSpan(NonceLength, ciphertextLength);
        var tag = data.AsSpan(NonceLength + ciphertextLength, TagLength);
        var aad = System.Text.Encoding.UTF8.GetBytes(sessionId);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_masterKey, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

        return plaintext;
    }
}
