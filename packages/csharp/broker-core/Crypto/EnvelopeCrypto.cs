using System.Security.Cryptography;
using System.Text;

namespace BrokerCore.Crypto;

/// <summary>
/// 信封加密/解密實作
/// ECDH P-256 金鑰交換 + HKDF 金鑰導出 + AES-256-GCM 對稱加密
///
/// 全部使用 .NET 8 內建 API，零外部加密依賴
///
/// 生命週期：Singleton（broker 啟動時生成或載入 ECDH 長期金鑰對）
/// </summary>
public class EnvelopeCrypto : IEnvelopeCrypto, IDisposable
{
    private readonly ECDiffieHellman _brokerEcdh;
    private readonly string _brokerPublicKeyBase64;

    private const string HkdfInfo = "broker-session-v1";
    private const int SessionKeyLength = 32; // AES-256
    private const int NonceLength = 12;      // GCM nonce
    private const int TagLength = 16;        // GCM tag

    /// <summary>
    /// 建構子：生成新的 ECDH P-256 金鑰對
    /// 生產環境應使用 LoadFromKey 載入預先生成的金鑰
    /// </summary>
    public EnvelopeCrypto()
    {
        _brokerEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _brokerPublicKeyBase64 = Convert.ToBase64String(
            _brokerEcdh.PublicKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    /// 從已有的私鑰載入（叢集化：所有 instance 共享同一金鑰對）
    /// </summary>
    /// <param name="privateKeyPkcs8Base64">PKCS#8 格式私鑰（Base64）</param>
    public EnvelopeCrypto(string privateKeyPkcs8Base64)
    {
        _brokerEcdh = ECDiffieHellman.Create();
        var keyBytes = Convert.FromBase64String(privateKeyPkcs8Base64);
        _brokerEcdh.ImportPkcs8PrivateKey(keyBytes, out _);
        _brokerPublicKeyBase64 = Convert.ToBase64String(
            _brokerEcdh.PublicKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>匯出私鑰（PKCS#8 Base64），供叢集化部署時分發</summary>
    public string ExportPrivateKeyBase64()
    {
        return Convert.ToBase64String(_brokerEcdh.ExportPkcs8PrivateKey());
    }

    /// <inheritdoc />
    public string GetBrokerPublicKey() => _brokerPublicKeyBase64;

    /// <inheritdoc />
    public byte[] DeriveSessionKey(string clientEphemeralPubBase64, string sessionId)
    {
        var clientPubBytes = Convert.FromBase64String(clientEphemeralPubBase64);

        using var clientEcdh = ECDiffieHellman.Create();
        clientEcdh.ImportSubjectPublicKeyInfo(clientPubBytes, out _);

        // ECDH 共享秘密
        var sharedSecret = _brokerEcdh.DeriveRawSecretAgreement(clientEcdh.PublicKey);

        try
        {
            // HKDF 導出 session key
            var salt = Encoding.UTF8.GetBytes(sessionId);
            var info = Encoding.UTF8.GetBytes(HkdfInfo);
            var sessionKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                sharedSecret,
                SessionKeyLength,
                salt,
                info);

            return sessionKey;
        }
        finally
        {
            // 清零共享秘密
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    /// <inheritdoc />
    public string DecryptHandshake(string clientEphemeralPubBase64, Envelope envelope)
    {
        // 初始交握：用 ECDH 導出的臨時金鑰解密
        // nonce 作為 HKDF salt（因為此時還沒有 session_id）
        var clientPubBytes = Convert.FromBase64String(clientEphemeralPubBase64);

        using var clientEcdh = ECDiffieHellman.Create();
        clientEcdh.ImportSubjectPublicKeyInfo(clientPubBytes, out _);

        var sharedSecret = _brokerEcdh.DeriveRawSecretAgreement(clientEcdh.PublicKey);

        try
        {
            // 初始交握用 nonce 作為 HKDF salt
            var nonceBytes = Convert.FromBase64String(envelope.Nonce);
            var info = Encoding.UTF8.GetBytes("broker-handshake-v1");
            var handshakeKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                sharedSecret,
                SessionKeyLength,
                nonceBytes,
                info);

            try
            {
                // AAD = client_ephemeral_pub + endpoint_path
                var aad = clientEphemeralPubBase64 + "/api/v1/sessions/register";
                return DecryptInternal(envelope, handshakeKey, aad);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(handshakeKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    /// <inheritdoc />
    public Envelope Encrypt(string plaintext, byte[] sessionKey, int seq, string aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagLength];
        var aadBytes = Encoding.UTF8.GetBytes(aad);

        using var aes = new AesGcm(sessionKey, TagLength);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aadBytes);

        return new Envelope
        {
            V = 1,
            Alg = "A256GCM",
            Seq = seq,
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };
    }

    /// <inheritdoc />
    public string Decrypt(Envelope envelope, byte[] sessionKey, string aad)
    {
        return DecryptInternal(envelope, sessionKey, aad);
    }

    // ── 內部方法 ──

    private static string DecryptInternal(Envelope envelope, byte[] key, string aad)
    {
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        var aadBytes = Encoding.UTF8.GetBytes(aad);

        if (nonce.Length != NonceLength)
            throw new CryptographicException($"Invalid nonce length: expected {NonceLength}, got {nonce.Length}");

        if (tag.Length != TagLength)
            throw new CryptographicException($"Invalid tag length: expected {TagLength}, got {tag.Length}");

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aadBytes);

        return Encoding.UTF8.GetString(plaintext);
    }

    public void Dispose()
    {
        _brokerEcdh.Dispose();
    }
}
