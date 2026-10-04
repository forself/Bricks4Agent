using Broker.Configuration;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Unit.Tests.Core;

/// <summary>
/// Registration credentials: only the SHA-256 of the secret is stored; verification tells missing, unknown,
/// mismatched, expired and revoked apart for the server log; a credential can be used repeatedly; seeds keep
/// their credential across restarts (with a fresh expiry) and replace it when the secret changes; every
/// credential expires; and the startup check refuses a seed without a usable secret outside development.
/// </summary>
public class RegistrationCredentialServiceTests
{
    private const string PrincipalId = "prn_cred";
    private const string TaskId = "task_cred";
    private const string SeedSecret = "seed-secret-for-unit-tests-0123456789abcdef";

    [Fact]
    public void Issue_StoresOnlyTheHashAndTheSecretVerifies()
    {
        WithService((db, service) =>
        {
            var issued = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));

            issued.Secret.Should().HaveLength(43, "32 random bytes in base64url without padding");
            issued.ToString().Should().NotContain(issued.Secret);
            var stored = db.Get<RegistrationCredential>(issued.CredentialId)!;
            stored.SecretHash.Should().Be(RegistrationCredentialService.ComputeHash(issued.Secret));
            stored.SecretHash.Should().NotContain(issued.Secret);

            var check = service.Verify(PrincipalId, TaskId, issued.Secret);
            check.Succeeded.Should().BeTrue();
            check.Credential!.CredentialId.Should().Be(issued.CredentialId);
        });
    }

    [Fact]
    public void Issue_GeneratesADifferentSecretEachTime()
    {
        WithService((_, service) =>
        {
            var first = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));
            var second = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));

            second.Secret.Should().NotBe(first.Secret);
            service.Verify(PrincipalId, TaskId, first.Secret).Succeeded.Should().BeTrue();
            service.Verify(PrincipalId, TaskId, second.Secret).Succeeded.Should().BeTrue();
        });
    }

    [Fact]
    public void Verify_ReportsWhyACredentialWasNotAccepted()
    {
        WithService((db, service) =>
        {
            var issued = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));
            var other = service.Issue("prn_other", "task_other", RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));

            service.Verify(PrincipalId, TaskId, null).Failure.Should().Be(RegistrationCredentialFailure.Missing);
            service.Verify(PrincipalId, TaskId, string.Empty).Failure.Should().Be(RegistrationCredentialFailure.Missing);
            service.Verify(PrincipalId, TaskId, RegistrationCredentialService.GenerateSecret()).Failure.Should().Be(RegistrationCredentialFailure.Mismatch);
            service.Verify(PrincipalId, TaskId, other.Secret).Failure.Should().Be(RegistrationCredentialFailure.Mismatch);
            service.Verify("prn_unknown", "task_unknown", issued.Secret).Failure.Should().Be(RegistrationCredentialFailure.Unknown);
            service.Verify(PrincipalId, "task_other", issued.Secret).Failure.Should().Be(RegistrationCredentialFailure.Unknown);
            service.Verify(string.Empty, TaskId, issued.Secret).Failure.Should().Be(RegistrationCredentialFailure.Unknown);
            service.Verify(PrincipalId, TaskId, new string('x', 600)).Failure.Should().Be(RegistrationCredentialFailure.Mismatch);

            db.Execute(
                "UPDATE registration_credentials SET expires_at = @past WHERE credential_id = @id",
                new { past = DateTime.UtcNow.AddMinutes(-1), id = issued.CredentialId });
            var expired = service.Verify(PrincipalId, TaskId, issued.Secret);
            expired.Failure.Should().Be(RegistrationCredentialFailure.Expired);
            expired.Succeeded.Should().BeFalse();
            expired.Credential!.CredentialId.Should().Be(issued.CredentialId, "the matching credential is reported for the audit trail");

            service.Revoke(other.CredentialId, "unit", "unit").Should().BeTrue();
            service.Revoke(other.CredentialId, "unit", "unit").Should().BeFalse("it is already revoked");
            service.Verify("prn_other", "task_other", other.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
        });
    }

    [Fact]
    public void RecordUse_CountsRegistrationsWithoutLimitingThem()
    {
        WithService((db, service) =>
        {
            var issued = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var check = service.Verify(PrincipalId, TaskId, issued.Secret);
                check.Succeeded.Should().BeTrue("a credential stays usable until it expires or is revoked");
                service.RecordUse(check.Credential!.CredentialId);
            }

            var stored = db.Get<RegistrationCredential>(issued.CredentialId)!;
            stored.UseCount.Should().Be(3);
            stored.LastUsedAt.Should().NotBeNull();
        });
    }

    [Fact]
    public void RevokeFor_CanBeLimitedToOneSource()
    {
        WithService((_, service) =>
        {
            var spawned = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));
            var adminIssued = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));

            service.RevokeFor(PrincipalId, TaskId, "respawn", "unit", RegistrationCredentialSources.AgentSpawn).Should().Be(1);
            service.Verify(PrincipalId, TaskId, spawned.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.Verify(PrincipalId, TaskId, adminIssued.Secret).Succeeded.Should().BeTrue();

            service.RevokeFor(PrincipalId, TaskId, "deactivate", "unit").Should().Be(1);
            service.Verify(PrincipalId, TaskId, adminIssued.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.List(PrincipalId, TaskId).Should().BeEmpty();
            service.List(PrincipalId, TaskId, includeInactive: true).Should().HaveCount(2);
        });
    }

    /// <summary>A successful respawn revokes the earlier spawn credentials but keeps the one it just issued.</summary>
    [Fact]
    public void RevokeFor_CanKeepOneCredential()
    {
        WithService((_, service) =>
        {
            var older = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));
            var previous = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));
            var current = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));
            var adminIssued = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));

            service.RevokeFor(PrincipalId, TaskId, "respawn", "unit", RegistrationCredentialSources.AgentSpawn, exceptCredentialId: current.CredentialId)
                .Should().Be(2);
            service.Verify(PrincipalId, TaskId, older.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.Verify(PrincipalId, TaskId, previous.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.Verify(PrincipalId, TaskId, current.Secret).Succeeded.Should().BeTrue();
            service.Verify(PrincipalId, TaskId, adminIssued.Secret).Succeeded.Should().BeTrue("another source is not touched");

            // Without a source limit, the kept credential is still the only exception.
            service.RevokeFor(PrincipalId, TaskId, "deactivate", "unit", exceptCredentialId: current.CredentialId).Should().Be(1);
            service.Verify(PrincipalId, TaskId, adminIssued.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.Verify(PrincipalId, TaskId, current.Secret).Succeeded.Should().BeTrue();
        });
    }

    /// <summary>
    /// Revoked and expired records (for example the ones failed spawns leave behind) never push a credential that
    /// is still valid out of what is compared: an older valid credential keeps verifying however many newer
    /// inactive records the same principal and task have, and the failure reasons are still told apart.
    /// </summary>
    [Fact]
    public void Verify_AcceptsAnOlderValidCredentialBehindManyNewerInactiveRecords()
    {
        WithService((db, service) =>
        {
            var valid = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));
            db.Execute(
                "UPDATE registration_credentials SET created_at = @earlier WHERE credential_id = @id",
                new { earlier = DateTime.UtcNow.AddHours(-1), id = valid.CredentialId });

            IssuedRegistrationCredential? lastRevoked = null;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                lastRevoked = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));
                service.Revoke(lastRevoked.CredentialId, "spawn failed", "unit").Should().BeTrue();
            }

            IssuedRegistrationCredential? lastExpired = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                lastExpired = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));
                db.Execute(
                    "UPDATE registration_credentials SET expires_at = @past WHERE credential_id = @id",
                    new { past = DateTime.UtcNow.AddMinutes(-1), id = lastExpired.CredentialId });
            }

            var check = service.Verify(PrincipalId, TaskId, valid.Secret);
            check.Failure.Should().Be(RegistrationCredentialFailure.None, "the credential is neither revoked nor expired");
            check.Credential!.CredentialId.Should().Be(valid.CredentialId);

            service.Verify(PrincipalId, TaskId, lastRevoked!.Secret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.Verify(PrincipalId, TaskId, lastExpired!.Secret).Failure.Should().Be(RegistrationCredentialFailure.Expired);
            service.Verify(PrincipalId, TaskId, RegistrationCredentialService.GenerateSecret()).Failure.Should().Be(RegistrationCredentialFailure.Mismatch);
            service.Verify(PrincipalId, TaskId, null).Failure.Should().Be(RegistrationCredentialFailure.Missing);
        });
    }

    [Fact]
    public void IsRevoked_ReportsRevokedAndUnknownCredentialsButNotExpiredOnes()
    {
        WithService((db, service) =>
        {
            var active = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));
            var revoked = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));
            var expired = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));
            service.Revoke(revoked.CredentialId, "unit", "unit");
            db.Execute(
                "UPDATE registration_credentials SET expires_at = @past WHERE credential_id = @id",
                new { past = DateTime.UtcNow.AddMinutes(-1), id = expired.CredentialId });

            service.IsRevoked(active.CredentialId).Should().BeFalse();
            service.IsRevoked(revoked.CredentialId).Should().BeTrue();
            service.IsRevoked(expired.CredentialId).Should().BeFalse("expiry only stops new registrations");
            service.IsRevoked("rgc_unknown").Should().BeTrue("an unknown credential is treated as revoked");
            service.IsRevoked(string.Empty).Should().BeTrue();
        });
    }

    [Fact]
    public void Issue_RequiresAnExpiryInTheFutureWithinTheMaximumLifetime()
    {
        WithService((_, service) =>
        {
            var past = () => service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddMinutes(-1));
            var tooLong = () => service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AdminIssue, "unit",
                DateTime.UtcNow.AddHours(RegistrationCredentialService.MaxLifetimeHours + 1));
            var noTask = () => service.Issue(PrincipalId, " ", RegistrationCredentialSources.AdminIssue, "unit", DateTime.UtcNow.AddHours(1));

            past.Should().Throw<ArgumentOutOfRangeException>();
            tooLong.Should().Throw<ArgumentOutOfRangeException>();
            noTask.Should().Throw<ArgumentException>();
        });
    }

    [Fact]
    public void UpsertSeed_KeepsTheSameSecretAndRenewsItsExpiry_ReplacesAChangedSecret()
    {
        WithService((db, service) =>
        {
            var first = service.UpsertSeed(PrincipalId, TaskId, SeedSecret, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1))!;
            var other = service.Issue(PrincipalId, TaskId, RegistrationCredentialSources.AgentSpawn, "unit", DateTime.UtcNow.AddHours(1));

            var restarted = service.UpsertSeed(PrincipalId, TaskId, SeedSecret, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(5));
            restarted!.CredentialId.Should().Be(first.CredentialId, "the same secret keeps its credential across restarts");
            db.Get<RegistrationCredential>(first.CredentialId)!.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(5), TimeSpan.FromSeconds(10));

            const string rotated = "rotated-seed-secret-for-unit-tests-0123456789";
            var replaced = service.UpsertSeed(PrincipalId, TaskId, rotated, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1));
            replaced!.CredentialId.Should().NotBe(first.CredentialId);
            service.Verify(PrincipalId, TaskId, SeedSecret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            service.Verify(PrincipalId, TaskId, rotated).Succeeded.Should().BeTrue();
            service.Verify(PrincipalId, TaskId, other.Secret).Succeeded.Should().BeTrue("other sources are left alone");

            var placeholder = () => service.UpsertSeed(PrincipalId, TaskId, "CHANGE_ME_0123456789012345678901234567890", RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1));
            var shortSecret = () => service.UpsertSeed(PrincipalId, TaskId, "too-short", RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1));
            placeholder.Should().Throw<ArgumentException>();
            shortSecret.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("too-short");
        });
    }

    [Fact]
    public void UpsertSeed_DoesNotRestoreACredentialAnOperatorRevoked_UntilTheSecretChanges()
    {
        WithService((db, service) =>
        {
            var seeded = service.UpsertSeed(PrincipalId, TaskId, SeedSecret, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1))!;
            service.Revoke(seeded.CredentialId, "Revoked by admin", "prn_admin").Should().BeTrue();

            // A restart with the same configured secret keeps the operator's revocation.
            service.UpsertSeed(PrincipalId, TaskId, SeedSecret, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1))
                .Should().BeNull("an operator's revocation survives a restart");
            service.Verify(PrincipalId, TaskId, SeedSecret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            db.Query<RegistrationCredential>("SELECT * FROM registration_credentials WHERE revoked_at IS NULL")
                .Should().BeEmpty("nothing is seeded again");

            // The same holds for a revocation of every credential of the task.
            const string rotated = "rotated-seed-secret-for-unit-tests-0123456789";
            var rotatedSeed = service.UpsertSeed(PrincipalId, TaskId, rotated, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1));
            rotatedSeed.Should().NotBeNull("a new secret is seeded");
            service.Verify(PrincipalId, TaskId, rotated).Succeeded.Should().BeTrue();
            service.RevokeFor(PrincipalId, TaskId, "Revoked by admin", "prn_admin").Should().Be(1);
            service.UpsertSeed(PrincipalId, TaskId, rotated, RegistrationCredentialSources.DevelopmentSeed, DateTime.UtcNow.AddHours(1))
                .Should().BeNull();
            service.Verify(PrincipalId, TaskId, rotated).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
        });
    }

    [Fact]
    public void Initializer_ReportsASeedCredentialHeldRevoked()
    {
        WithService((db, service) =>
        {
            var seed = Seed(SeedSecret);
            var initializer = new BrokerDbInitializer(db);
            initializer.Initialize(seed);
            initializer.SeedCredentialHeldRevoked.Should().BeFalse();
            var seeded = service.Verify(PrincipalId, TaskId, SeedSecret).Credential!;

            service.Revoke(seeded.CredentialId, "Revoked by admin", "prn_admin").Should().BeTrue();
            initializer.Initialize(seed);
            initializer.SeedCredentialHeldRevoked.Should().BeTrue();
            service.Verify(PrincipalId, TaskId, SeedSecret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);

            // Rotating the secret restores registration.
            const string rotated = "rotated-seed-secret-for-unit-tests-0123456789";
            initializer.Initialize(Seed(rotated));
            initializer.SeedCredentialHeldRevoked.Should().BeFalse();
            service.Verify(PrincipalId, TaskId, rotated).Succeeded.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("short", false)]
    [InlineData("CHANGE_ME_PLEASE_0123456789012345678901234", false)]
    [InlineData("REPLACE_WITH_DEVELOPMENT_REGISTRATION_SECRET", false)]
    [InlineData("0123456789012345678901234567890", false)]
    [InlineData("01234567890123456789012345678901", true)]
    public void IsUsableSecret_RejectsPlaceholdersAndShortValues(string? secret, bool usable)
        => RegistrationCredentialService.IsUsableSecret(secret).Should().Be(usable);

    // ── Seeds through the database initializer ─────────────────────────────────────────

    [Fact]
    public void Initializer_SeedsTheCredential_RenewsItOnRestart_AndRevokesItWhenTheSecretIsRemoved()
    {
        WithService((db, service) =>
        {
            var seed = Seed(SeedSecret);
            new BrokerDbInitializer(db).Initialize(seed);

            var first = service.Verify(PrincipalId, TaskId, SeedSecret);
            first.Succeeded.Should().BeTrue();
            first.Credential!.Source.Should().Be(RegistrationCredentialSources.DevelopmentSeed);
            first.Credential.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(seed.RegistrationSecretLifetimeHours), TimeSpan.FromMinutes(1));

            // Expire it, then "restart": the same secret gets its expiry back.
            db.Execute("UPDATE registration_credentials SET expires_at = @past", new { past = DateTime.UtcNow.AddMinutes(-1) });
            service.Verify(PrincipalId, TaskId, SeedSecret).Failure.Should().Be(RegistrationCredentialFailure.Expired);
            new BrokerDbInitializer(db).Initialize(seed);
            var renewed = service.Verify(PrincipalId, TaskId, SeedSecret);
            renewed.Succeeded.Should().BeTrue();
            renewed.Credential!.CredentialId.Should().Be(first.Credential.CredentialId);

            // The dashboard seed manages its own credential; it does not touch the development seed's.
            new BrokerDbInitializer(db).Initialize(Seed(string.Empty), RegistrationCredentialSources.DashboardSeed);
            service.Verify(PrincipalId, TaskId, SeedSecret).Succeeded.Should().BeTrue();

            new BrokerDbInitializer(db).Initialize(Seed(string.Empty));
            service.Verify(PrincipalId, TaskId, SeedSecret).Failure.Should().Be(RegistrationCredentialFailure.Revoked);
            db.Query<RegistrationCredential>("SELECT * FROM registration_credentials")
                .Should().OnlyContain(credential => credential.SecretHash != SeedSecret);

            // Configuring the same secret again creates a new credential; the active one wins over the revoked one.
            new BrokerDbInitializer(db).Initialize(seed);
            var reseeded = service.Verify(PrincipalId, TaskId, SeedSecret);
            reseeded.Succeeded.Should().BeTrue();
            reseeded.Credential!.CredentialId.Should().NotBe(first.Credential.CredentialId);
        });
    }

    [Fact]
    public void Initializer_AddsTheCredentialTableAndIndexToAnExistingDatabase()
    {
        WithDatabase(db =>
        {
            // An existing database from before registration credentials.
            db.EnsureTable<ContainerSession>();
            db.Query<TableInfo>("SELECT name AS Name FROM sqlite_master WHERE type = 'table' AND name = 'registration_credentials'")
                .Should().BeEmpty();

            new BrokerDbInitializer(db).Initialize();

            db.Query<TableInfo>("SELECT name AS Name FROM sqlite_master WHERE type = 'table' AND name = 'registration_credentials'")
                .Should().ContainSingle();
            db.Query<TableInfo>("PRAGMA index_list(registration_credentials)").Select(index => index.Name)
                .Should().Contain("idx_registration_credentials_subject");
        }, initialize: false);
    }

    // ── Startup check ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void SeedValidator_RefusesADevelopmentSeedWithoutAUsableSecretOutsideDevelopment(string environment)
    {
        var logger = new CapturingLogger();

        var missing = () => RegistrationSeedValidator.Validate(Seed(string.Empty), RegistrationSeedValidator.DevelopmentSeedSection, environment, logger);
        var shortValue = () => RegistrationSeedValidator.Validate(Seed("short-seed-value"), RegistrationSeedValidator.DevelopmentSeedSection, environment, logger);

        missing.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("DevelopmentSeed:RegistrationSecret");
        shortValue.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("short-seed-value");
        RegistrationSeedValidator.Validate(Seed(SeedSecret), RegistrationSeedValidator.DevelopmentSeedSection, environment, logger)
            .Should().BeTrue();
        logger.Entries.Should().NotContain(entry => entry.Message.Contains(SeedSecret));
    }

    [Theory]
    [InlineData("Development", "DevelopmentSeed")]
    [InlineData("Testing", "DevelopmentSeed")]
    [InlineData("Development", "DashboardSeed")]
    public void SeedValidator_OnlyWarnsInDevelopmentOrForTheDashboardSeed(string environment, string section)
    {
        var logger = new CapturingLogger();

        RegistrationSeedValidator.Validate(Seed("short-seed-value"), section, environment, logger).Should().BeFalse();

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
        logger.Entries.Should().NotContain(entry => entry.Message.Contains("short-seed-value"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RegistrationCredentialService.MaxLifetimeHours + 1)]
    public void SeedValidator_RefusesAnOutOfRangeLifetime(int hours)
    {
        var seed = Seed(SeedSecret);
        seed.RegistrationSecretLifetimeHours = hours;

        var validate = () => RegistrationSeedValidator.Validate(seed, RegistrationSeedValidator.DevelopmentSeedSection, "Development", new CapturingLogger());

        validate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SeedValidator_IgnoresADisabledSeed()
    {
        var seed = Seed(string.Empty);
        seed.Enabled = false;

        RegistrationSeedValidator.Validate(seed, RegistrationSeedValidator.DevelopmentSeedSection, "Production", new CapturingLogger())
            .Should().BeFalse();
        RegistrationSeedValidator.Validate(null, RegistrationSeedValidator.DevelopmentSeedSection, "Production", new CapturingLogger())
            .Should().BeFalse();
    }

    [Fact]
    public void Options_RejectOutOfRangeLifetimes()
    {
        new RegistrationCredentialOptions().Invoking(options => options.Validate()).Should().NotThrow();
        new RegistrationCredentialOptions { SpawnedAgentLifetimeHours = 0 }.Invoking(options => options.Validate())
            .Should().Throw<ArgumentOutOfRangeException>();
        new RegistrationCredentialOptions { AdminIssuedLifetimeHours = RegistrationCredentialService.MaxLifetimeHours + 1 }
            .Invoking(options => options.Validate())
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SeedOptions_ToStringDoesNotRevealTheSecret()
        => Seed(SeedSecret).ToString().Should().NotContain(SeedSecret);

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private static DevelopmentSeedOptions Seed(string secret) => new()
    {
        Enabled = true,
        PrincipalId = PrincipalId,
        TaskId = TaskId,
        TaskType = "analysis",
        AssignedRoleId = "role_reader",
        RegistrationSecret = secret
    };

    private static void WithService(Action<BrokerDb, RegistrationCredentialService> test)
        => WithDatabase(db => test(db, new RegistrationCredentialService(db)));

    private static void WithDatabase(Action<BrokerDb> test, bool initialize = true)
    {
        var path = Path.Combine(Path.GetTempPath(), $"broker_regcred_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new BrokerDb($"Data Source={path}"))
            {
                if (initialize)
                {
                    new BrokerDbInitializer(db).Initialize();
                }

                test(db);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-shm", path + "-wal" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // Best effort; the file lives in the temp directory.
                }
            }
        }
    }

    private sealed class TableInfo
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
