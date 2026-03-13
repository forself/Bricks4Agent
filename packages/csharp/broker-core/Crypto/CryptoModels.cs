namespace BrokerCore.Crypto;

/// <summary>加密信封（所有 POST body 的統一格式）</summary>
public class Envelope
{
    /// <summary>協議版本</summary>
    public int V { get; set; } = 1;

    /// <summary>演算法（"A256GCM" 或 "ECDH-ES+A256GCM"）</summary>
    public string Alg { get; set; } = "A256GCM";

    /// <summary>訊息序號（防 replay）</summary>
    public int Seq { get; set; }

    /// <summary>Nonce（Base64，12 bytes）</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>密文（Base64）</summary>
    public string Ciphertext { get; set; } = string.Empty;

    /// <summary>GCM Authentication Tag（Base64，16 bytes）</summary>
    public string Tag { get; set; } = string.Empty;
}

/// <summary>加密請求外層（包含 session 路由資訊 + 信封）</summary>
public class EncryptedRequest
{
    public int V { get; set; } = 1;

    /// <summary>Session ID（明文，用於路由到正確的 session_key）</summary>
    public string? SessionId { get; set; }

    /// <summary>Client 臨時 ECDH 公鑰（Base64 DER，僅初始交握時使用）</summary>
    public string? ClientEphemeralPub { get; set; }

    /// <summary>加密信封</summary>
    public Envelope Envelope { get; set; } = new();
}

/// <summary>加密回應</summary>
public class EncryptedResponse
{
    public int V { get; set; } = 1;

    /// <summary>
    /// Session ID（明文，僅初始交握回應時包含）。
    /// Client 需要此值來 derive session_key，才能解密 Envelope。
    /// </summary>
    public string? SessionId { get; set; }

    public Envelope Envelope { get; set; } = new();
}

/// <summary>Session 加密狀態（外部化於 DB）</summary>
public class SessionCryptoState
{
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Session 對稱金鑰（僅存在於記憶體，DB 存加密版本）</summary>
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();

    /// <summary>最後確認的序號</summary>
    public int LastSeenSeq { get; set; }
}

/// <summary>ECDH 金鑰對</summary>
public class EcdhKeyPair
{
    /// <summary>公鑰（Base64 DER）</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>私鑰（原始 bytes，不持久化至 DB）</summary>
    public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
}
