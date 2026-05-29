using System.Text;
using Broker.Services;
using FluentAssertions;

namespace Unit.Tests.Services;

/// <summary>
/// 鎖住 broker 簽章 artifact 下載 API 的安全契約(2026-05-29, AnthonyLee)。
/// 純 internal static、無 DI/DB 依賴。涵蓋:簽章確定性、驗簽(過期/竄改/換id/空)、路徑穿透防護。
/// </summary>
public class ArtifactDownloadSecurityTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("unit-test-signing-secret-32bytes!");
    private const long Far = 9999999999;  // 遠未來 exp

    // ── 簽章確定性 ──
    [Fact]
    public void ComputeSignature_IsDeterministic()
    {
        var a = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", 1000);
        var b = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", 1000);
        a.Should().Be(b).And.NotBeNullOrEmpty();
    }

    [Fact]
    public void ComputeSignature_DiffersByIdAndExp()
    {
        var baseSig = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", 1000);
        BrokerArtifactDownloadService.ComputeSignature(Secret, "art-2", 1000).Should().NotBe(baseSig, "不同 id 必不同簽");
        BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", 2000).Should().NotBe(baseSig, "不同 exp 必不同簽");
    }

    [Fact]
    public void ComputeSignature_DiffersBySecret()
    {
        var other = Encoding.UTF8.GetBytes("a-completely-different-secret-key");
        BrokerArtifactDownloadService.ComputeSignature(other, "art-1", 1000)
            .Should().NotBe(BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", 1000));
    }

    // ── 驗簽 ──
    [Fact]
    public void Validate_GoodSig_NotExpired_Passes()
    {
        var sig = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", Far);
        BrokerArtifactDownloadService.ValidateSignatureCore(Secret, "art-1", Far, sig, nowUnix: 1000)
            .Should().BeTrue();
    }

    [Fact]
    public void Validate_Expired_FailsEvenWithGoodSig()
    {
        var sig = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", 1000);
        BrokerArtifactDownloadService.ValidateSignatureCore(Secret, "art-1", 1000, sig, nowUnix: 1001)
            .Should().BeFalse("now > exp = 過期、即使簽章正確也拒絕");
    }

    [Fact]
    public void Validate_TamperedSig_Fails()
    {
        var sig = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", Far);
        BrokerArtifactDownloadService.ValidateSignatureCore(Secret, "art-1", Far, sig + "x", nowUnix: 1000)
            .Should().BeFalse();
    }

    [Fact]
    public void Validate_SigForDifferentId_Fails()
    {
        // 拿 art-1 的簽去下載 art-2 → 必須失敗(防換 id)
        var sigForArt1 = BrokerArtifactDownloadService.ComputeSignature(Secret, "art-1", Far);
        BrokerArtifactDownloadService.ValidateSignatureCore(Secret, "art-2", Far, sigForArt1, nowUnix: 1000)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptySig_Fails(string? sig)
    {
        BrokerArtifactDownloadService.ValidateSignatureCore(Secret, "art-1", Far, sig, nowUnix: 1000)
            .Should().BeFalse();
    }

    // ── 路徑穿透防護 ──
    [Fact]
    public void PathUnderRoot_FileInsideRoot_True()
    {
        BrokerArtifactDownloadService.IsPathUnderRoot(
            Path.Combine("C:", "data", "users", "u1", "documents", "report.pdf"),
            Path.Combine("C:", "data", "users", "u1", "documents")).Should().BeTrue();
    }

    [Fact]
    public void PathUnderRoot_FileOutsideRoot_False()
    {
        BrokerArtifactDownloadService.IsPathUnderRoot(
            Path.Combine("C:", "windows", "system32", "config", "sam"),
            Path.Combine("C:", "data", "users", "u1", "documents")).Should().BeFalse("escape 出 root 必拒");
    }

    [Fact]
    public void PathUnderRoot_SiblingPrefixAttack_False()
    {
        // /data/users/u1/documents-evil 不該被當成在 /data/users/u1/documents 之下(前綴繞過)
        BrokerArtifactDownloadService.IsPathUnderRoot(
            Path.Combine("C:", "data", "users", "u1", "documents-evil", "x.pdf"),
            Path.Combine("C:", "data", "users", "u1", "documents")).Should().BeFalse();
    }
}
