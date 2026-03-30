using BrokerCore.Services;
using FluentAssertions;

namespace Unit.Tests.Crypto;

public class ScopedTokenServiceTests
{
    private readonly ScopedTokenService _sut = new(
        secret: "test-signing-key-at-least-32-chars-long!!",
        issuer: "test-broker",
        audience: "test-agent",
        expirationMinutes: 15);

    private static ScopedTokenClaims MakeClaims(
        string principalId = "p-1",
        string sessionId = "sess-1",
        string taskId = "task-1",
        int epoch = 1) => new()
    {
        PrincipalId = principalId,
        Jti = $"jti_{Guid.NewGuid():N}",
        TaskId = taskId,
        SessionId = sessionId,
        RoleId = "role-operator",
        CapabilityIds = new[] { "file.read", "file.write" },
        Scope = "{}",
        Epoch = epoch
    };

    [Fact]
    public void GenerateToken_ReturnsJwt()
    {
        var token = _sut.GenerateToken(MakeClaims());
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3, "JWT has 3 dot-separated parts");
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsClaims()
    {
        var token = _sut.GenerateToken(MakeClaims(principalId: "p-1", sessionId: "sess-1"));
        var claims = _sut.ValidateToken(token);

        claims.Should().NotBeNull();
        claims!.PrincipalId.Should().Be("p-1");
        claims.SessionId.Should().Be("sess-1");
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var token = _sut.GenerateToken(MakeClaims());
        var tampered = token + "x";
        _sut.ValidateToken(tampered).Should().BeNull();
    }

    [Fact]
    public void ValidateToken_MalformedString_ThrowsOrReturnsNull()
    {
        // ScopedTokenService.ValidateToken 對非 JWT 格式可能拋 SecurityTokenMalformedException
        // 這是合理行為：呼叫端（中介軟體）應處理例外
        var act = () => _sut.ValidateToken("not-a-valid-jwt");
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void GenerateToken_DifferentClaims_DifferentTokens()
    {
        var t1 = _sut.GenerateToken(MakeClaims(principalId: "p-1"));
        var t2 = _sut.GenerateToken(MakeClaims(principalId: "p-2"));
        t1.Should().NotBe(t2);
    }

    [Fact]
    public void ValidateToken_WrongSecret_ReturnsNull()
    {
        var token = _sut.GenerateToken(MakeClaims());
        var other = new ScopedTokenService(
            secret: "different-signing-key-at-least-32-chars!!",
            issuer: "test-broker",
            audience: "test-agent");
        other.ValidateToken(token).Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WrongAudience_ReturnsNull()
    {
        var token = _sut.GenerateToken(MakeClaims());
        var other = new ScopedTokenService(
            secret: "test-signing-key-at-least-32-chars-long!!",
            issuer: "test-broker",
            audience: "wrong-audience");
        other.ValidateToken(token).Should().BeNull();
    }
}
