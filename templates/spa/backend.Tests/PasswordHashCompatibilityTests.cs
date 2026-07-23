using FluentAssertions;
using SpaApi.Data;
using Xunit;

namespace SpaApi.Template.Tests;

public sealed class PasswordHashCompatibilityTests
{
    private const string ExistingStoredHash =
        "100000.AAECAwQFBgcICQoLDA0ODw==.celApUIiS5mYQwT1sMFDqqTwPUF/HvEDq30OrHEWjfc=";

    [Fact]
    public void Password_verifier_accepts_hashes_created_before_the_dotnet10_migration()
    {
        BCryptHelper.VerifyPassword("Compatibility-Passw0rd!", ExistingStoredHash).Should().BeTrue();
        BCryptHelper.VerifyPassword("wrong-password", ExistingStoredHash).Should().BeFalse();
    }

    [Fact]
    public void Newly_created_hashes_keep_the_existing_storage_format()
    {
        var storedHash = BCryptHelper.HashPassword("new-password");

        storedHash.Split('.').Should().HaveCount(3);
        storedHash.Should().StartWith("100000.");
        BCryptHelper.VerifyPassword("new-password", storedHash).Should().BeTrue();
    }
}
