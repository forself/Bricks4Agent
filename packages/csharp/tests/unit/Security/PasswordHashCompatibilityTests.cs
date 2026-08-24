using System.Reflection;
using Bricks4Agent.Security.Mfa;
using Broker.Services;
using FluentAssertions;

namespace Unit.Tests.Security;

public sealed class PasswordHashCompatibilityTests
{
    private const string Password = "Compatibility-Passw0rd!";
    private const string SaltBase64 = "AAECAwQFBgcICQoLDA0ODw==";
    private const string HashAt100000 = "celApUIiS5mYQwT1sMFDqqTwPUF/HvEDq30OrHEWjfc=";
    private const string HashAt120000 = "T6jUPFZ4QR/ZgEWzPS2fbRJia5OQhj979pc3uNPBQ/E=";

    [Theory]
    [InlineData(typeof(LocalAdminAuthService))]
    [InlineData(typeof(PortalAuthService))]
    public void Broker_password_hashing_matches_existing_120000_iteration_vector(Type serviceType)
    {
        var salt = Convert.FromBase64String(SaltBase64);
        var hashMethod = serviceType.GetMethod(
            "HashPassword",
            BindingFlags.NonPublic | BindingFlags.Static);

        hashMethod.Should().NotBeNull();
        var actual = hashMethod!.Invoke(null, [Password, salt, 120000]).Should().BeOfType<byte[]>().Subject;

        Convert.ToBase64String(actual).Should().Be(HashAt120000);
    }

    [Fact]
    public void Mfa_password_verifier_accepts_existing_100000_iteration_hash()
    {
        var storedHash = $"100000.{SaltBase64}.{HashAt100000}";
        var verifyMethod = typeof(MfaAuthService).GetMethod(
            "VerifyPassword",
            BindingFlags.NonPublic | BindingFlags.Static);

        verifyMethod.Should().NotBeNull();
        verifyMethod!.Invoke(null, [Password, storedHash]).Should().Be(true);
        verifyMethod.Invoke(null, ["wrong-password", storedHash]).Should().Be(false);
    }
}
