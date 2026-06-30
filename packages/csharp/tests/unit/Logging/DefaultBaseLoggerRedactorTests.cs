using BaseLogger;
using FluentAssertions;
using Xunit;

namespace Unit.Tests.Logging;

/// <summary>
/// DefaultBaseLoggerRedactor 確定性測試(spec §9)。
/// 鎖住:敏感名遮罩、識別子穩定雜湊、長值截斷、行內 secret 移除、system principal 例外。
/// </summary>
public class DefaultBaseLoggerRedactorTests
{
    private readonly DefaultBaseLoggerRedactor _r = new();

    [Theory]
    [InlineData("access_token", true)]
    [InlineData("Authorization", true)]
    [InlineData("api_key", true)]
    [InlineData("user_password", true)]
    [InlineData("set-cookie", true)]
    [InlineData("username", false)]
    [InlineData("worker_id", false)]
    [InlineData("note", false)]
    public void IsSensitiveName_DetectsSecretLikeNames(string name, bool expected)
        => _r.IsSensitiveName(name).Should().Be(expected);

    [Fact]
    public void RedactValue_MasksSensitiveName()
        => _r.RedactValue("access_token", "abc.def.ghi").Should().Be(DefaultBaseLoggerRedactor.Mask);

    [Fact]
    public void RedactValue_HashesIdentifier_NotLeakingRaw()
    {
        var redacted = _r.RedactValue("user_id", "U1234567890");
        redacted.Should().StartWith("h:");
        redacted.Should().NotContain("U1234567890");
    }

    [Fact]
    public void RedactValue_TruncatesLongNonSensitiveValue()
    {
        var redacted = _r.RedactValue("note", new string('a', 600));
        redacted.Should().Contain("[truncated]");
        redacted.Length.Should().BeLessThan(600);
    }

    [Fact]
    public void RedactValue_KeepsShortNormalValue()
        => _r.RedactValue("note", "hello").Should().Be("hello");

    [Fact]
    public void RedactProperties_RedactsEachByName()
    {
        var props = new Dictionary<string, object?>
        {
            ["secret_key"] = "s3cr3t",
            ["session_id"] = "S-999",
            ["status"] = "ok",
        };

        var red = _r.RedactProperties(props);

        red["secret_key"].Should().Be(DefaultBaseLoggerRedactor.Mask);
        red["session_id"].Should().StartWith("h:");
        red["status"].Should().Be("ok");
    }

    [Theory]
    [InlineData("Authorization: Bearer abc123def456", "abc123def456")]
    [InlineData("token=SECRETVALUE", "SECRETVALUE")]
    [InlineData("password: hunter2", "hunter2")]
    [InlineData("login with Bearer zzz999", "zzz999")]
    public void RedactText_StripsInlineSecret(string input, string leaked)
    {
        var red = _r.RedactText(input);
        red.Should().Contain(DefaultBaseLoggerRedactor.Mask);
        red.Should().NotContain(leaked);
    }

    [Fact]
    public void RedactText_LeavesCleanTextUntouched()
        => _r.RedactText("worker trading-wkr-1 reconnected").Should().Be("worker trading-wkr-1 reconnected");

    [Fact]
    public void RedactPrincipalId_KeepsSystemPrincipalPlain()
        => _r.RedactPrincipalId("system_rag_ingest").Should().Be("system_rag_ingest");

    [Fact]
    public void RedactPrincipalId_HashesRealUser()
    {
        var redacted = _r.RedactPrincipalId("U_real_user");
        redacted.Should().StartWith("h:");
        redacted.Should().NotContain("U_real_user");
    }

    [Fact]
    public void HashIdentifier_IsDeterministicAndDistinct()
    {
        _r.HashIdentifier("abc").Should().Be(_r.HashIdentifier("abc"));
        _r.HashIdentifier("abc").Should().NotBe(_r.HashIdentifier("xyz"));
    }
}
