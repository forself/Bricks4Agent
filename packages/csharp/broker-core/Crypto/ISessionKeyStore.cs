namespace BrokerCore.Crypto;

/// <summary>
/// Session 金鑰存取介面（可替換後端）
/// Phase 1: DbSessionKeyStore（DB 外部化）
/// Phase 2: RedisSessionKeyStore（Redis + DB fallback）
/// 叢集化核心：所有 broker instance 共享同一後端
/// </summary>
public interface ISessionKeyStore
{
    /// <summary>儲存 session 金鑰（加密後存入外部儲存）</summary>
    void Store(string sessionId, byte[] sessionKey);

    /// <summary>取得 session 金鑰（從外部儲存解密載入）</summary>
    byte[]? Retrieve(string sessionId);

    /// <summary>移除 session 金鑰</summary>
    void Remove(string sessionId);

    /// <summary>
    /// 原子更新 replay 序號（防並發）
    /// 回傳 true = 序號有效（大於 last_seen），已更新
    /// 回傳 false = replay 攻擊（序號 <= last_seen）
    /// </summary>
    bool TryAdvanceSeq(string sessionId, int newSeq);
}
