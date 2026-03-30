using BrokerCore.Crypto;
using FluentAssertions;

namespace Unit.Tests.Crypto;

public class EnvelopeCryptoTests : IDisposable
{
    private readonly EnvelopeCrypto _broker = new();

    public void Dispose() => _broker.Dispose();

    // --- 金鑰管理 ---

    [Fact]
    public void GetBrokerPublicKey_ReturnsValidBase64()
    {
        var pubKey = _broker.GetBrokerPublicKey();
        pubKey.Should().NotBeNullOrEmpty();
        var act = () => Convert.FromBase64String(pubKey);
        act.Should().NotThrow();
    }

    [Fact]
    public void ExportPrivateKey_CanBeReloaded()
    {
        var exported = _broker.ExportPrivateKeyBase64();
        using var reloaded = new EnvelopeCrypto(exported);
        reloaded.GetBrokerPublicKey().Should().Be(_broker.GetBrokerPublicKey());
    }

    [Fact]
    public void TwoInstances_HaveDifferentKeys()
    {
        using var other = new EnvelopeCrypto();
        other.GetBrokerPublicKey().Should().NotBe(_broker.GetBrokerPublicKey());
    }

    // --- ECDH 金鑰導出 ---

    [Fact]
    public void DeriveSessionKey_Returns32Bytes()
    {
        using var client = new EnvelopeCrypto();
        var sessionKey = _broker.DeriveSessionKey(client.GetBrokerPublicKey(), "test-session-1");
        sessionKey.Should().NotBeNullOrEmpty();
        sessionKey.Length.Should().Be(32, "AES-256 requires 32-byte key");
    }

    [Fact]
    public void DeriveSessionKey_DifferentSessionIds_DifferentKeys()
    {
        using var client = new EnvelopeCrypto();
        var clientPub = client.GetBrokerPublicKey();
        var key1 = _broker.DeriveSessionKey(clientPub, "session-1");
        var key2 = _broker.DeriveSessionKey(clientPub, "session-2");
        key1.Should().NotEqual(key2);
    }

    // --- AES-256-GCM 加解密 ---

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var sessionKey = DeriveTestKey();
        var aad = "req:sess1:1:/api/test";

        var envelope = _broker.Encrypt("Hello, World!", sessionKey, 1, aad);
        var decrypted = _broker.Decrypt(envelope, sessionKey, aad);

        decrypted.Should().Be("Hello, World!");
    }

    [Fact]
    public void Encrypt_ProducesNonDeterministicNonce()
    {
        var sessionKey = DeriveTestKey();
        var env1 = _broker.Encrypt("Same message", sessionKey, 1, "aad");
        var env2 = _broker.Encrypt("Same message", sessionKey, 2, "aad");
        env1.Nonce.Should().NotBe(env2.Nonce);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var sessionKey = DeriveTestKey();
        var envelope = _broker.Encrypt("Secret", sessionKey, 1, "aad");

        var bytes = Convert.FromBase64String(envelope.Ciphertext);
        bytes[0] ^= 0xFF;
        envelope.Ciphertext = Convert.ToBase64String(bytes);

        var act = () => _broker.Decrypt(envelope, sessionKey, "aad");
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var key1 = DeriveTestKey();
        var key2 = DeriveTestKey();
        var envelope = _broker.Encrypt("Secret", key1, 1, "aad");

        var act = () => _broker.Decrypt(envelope, key2, "aad");
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Decrypt_WrongAad_Throws()
    {
        var sessionKey = DeriveTestKey();
        var envelope = _broker.Encrypt("Secret", sessionKey, 1, "req:sess1:1:/api");

        var act = () => _broker.Decrypt(envelope, sessionKey, "resp:sess1:1:/api");
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void EncryptDecrypt_EmptyMessage_Works()
    {
        var sessionKey = DeriveTestKey();
        var envelope = _broker.Encrypt("", sessionKey, 1, "aad");
        var decrypted = _broker.Decrypt(envelope, sessionKey, "aad");
        decrypted.Should().Be("");
    }

    [Fact]
    public void EncryptDecrypt_LargeMessage_Works()
    {
        var sessionKey = DeriveTestKey();
        var large = new string('A', 1_000_000);
        var envelope = _broker.Encrypt(large, sessionKey, 1, "aad");
        var decrypted = _broker.Decrypt(envelope, sessionKey, "aad");
        decrypted.Should().Be(large);
    }

    // --- Envelope 結構 ---

    [Fact]
    public void Envelope_HasCorrectStructure()
    {
        var sessionKey = DeriveTestKey();
        var envelope = _broker.Encrypt("test", sessionKey, 42, "aad");
        envelope.Alg.Should().Be("A256GCM");
        envelope.Nonce.Should().NotBeNullOrEmpty();
        envelope.Ciphertext.Should().NotBeNullOrEmpty();
        envelope.Tag.Should().NotBeNullOrEmpty();
        envelope.Seq.Should().Be(42);
        envelope.V.Should().Be(1);
    }

    // --- AAD 方向標記 ---

    [Fact]
    public void Aad_ReqVsResp_AreNotInterchangeable()
    {
        var sessionKey = DeriveTestKey();
        var envelope = _broker.Encrypt("payload", sessionKey, 1, "req:sess1:1:/api/test");
        var act = () => _broker.Decrypt(envelope, sessionKey, "resp:sess1:1:/api/test");
        act.Should().Throw<Exception>();
    }

    // --- 輔助方法 ---

    private byte[] DeriveTestKey()
    {
        using var client = new EnvelopeCrypto();
        return _broker.DeriveSessionKey(client.GetBrokerPublicKey(), $"test-{Guid.NewGuid():N}");
    }
}
