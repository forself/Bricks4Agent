namespace BrokerCore.Crypto;

/// <summary>
/// 信封加密/解密介面
/// 所有通訊經此層 —— ECDH 金鑰交換 + AES-256-GCM 對稱加密
/// </summary>
public interface IEnvelopeCrypto
{
    /// <summary>取得 broker 的 ECDH 公鑰（Base64 DER，供 client 預置）</summary>
    string GetBrokerPublicKey();

    /// <summary>
    /// 處理初始交握：client 送來 ECDH 公鑰 → 導出 session_key
    /// </summary>
    /// <param name="clientEphemeralPubBase64">Client 的臨時 ECDH 公鑰（Base64 DER）</param>
    /// <param name="sessionId">新 session ID（作為 HKDF salt）</param>
    /// <returns>導出的 session 對稱金鑰（32 bytes）</returns>
    byte[] DeriveSessionKey(string clientEphemeralPubBase64, string sessionId);

    /// <summary>
    /// 用初始交握金鑰解密（session 註冊請求）
    /// </summary>
    string DecryptHandshake(string clientEphemeralPubBase64, Envelope envelope);

    /// <summary>AES-256-GCM 加密</summary>
    /// <param name="plaintext">明文</param>
    /// <param name="sessionKey">對稱金鑰（32 bytes）</param>
    /// <param name="seq">訊息序號</param>
    /// <param name="aad">Additional Authenticated Data</param>
    Envelope Encrypt(string plaintext, byte[] sessionKey, int seq, string aad);

    /// <summary>AES-256-GCM 解密</summary>
    /// <param name="envelope">加密信封</param>
    /// <param name="sessionKey">對稱金鑰（32 bytes）</param>
    /// <param name="aad">Additional Authenticated Data</param>
    string Decrypt(Envelope envelope, byte[] sessionKey, string aad);
}
