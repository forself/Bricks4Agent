using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Unit.Tests.Core;

/// <summary>
/// Session lifetime rules: heartbeat extends only active, unexpired sessions, never past the maximum
/// lifetime and never shortening the current expiry; the lifetime settings are validated at startup;
/// and the database initializer adds the revocation lookup index to existing databases.
/// </summary>
public class SessionLifetimeTests
{
    [Fact]
    public void RegisterSession_ExpiresAfterTheConfiguredTtl()
    {
        WithDatabase(db =>
        {
            var service = new SessionService(db, new SessionLifetimeOptions { TtlMinutes = 30, MaxLifetimeMinutes = 120 });

            var session = service.RegisterSession("task_life", "prn_life", "role_reader", "jti_life", 1, string.Empty);

            session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
        });
    }

    [Fact]
    public void Heartbeat_ExtendsAnActiveSessionAndRecordsTheNewToken()
    {
        WithDatabase(db =>
        {
            var service = new SessionService(db, new SessionLifetimeOptions { TtlMinutes = 30, MaxLifetimeMinutes = 120 });
            var session = service.RegisterSession("task_life", "prn_life", "role_reader", "jti_first", 1, string.Empty);
            SetExpiry(db, session.SessionId, DateTime.UtcNow.AddMinutes(2));

            var extended = service.Heartbeat(session.SessionId, "jti_second");

            extended.Should().NotBeNull();
            extended!.Value.Kind.Should().Be(DateTimeKind.Utc);
            extended.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
            var stored = service.GetSession(session.SessionId)!;
            stored.ExpiresAt.Should().BeCloseTo(extended.Value, TimeSpan.FromSeconds(1));
            stored.TokenJti.Should().Be("jti_second");
        });
    }

    [Fact]
    public void Heartbeat_DoesNotReviveAnExpiredSession()
    {
        WithDatabase(db =>
        {
            var service = new SessionService(db);
            var session = service.RegisterSession("task_life", "prn_life", "role_reader", "jti_life", 1, string.Empty);
            var past = DateTime.UtcNow.AddMinutes(-1);
            SetExpiry(db, session.SessionId, past);

            service.Heartbeat(session.SessionId).Should().BeNull();
            service.GetSession(session.SessionId)!.ExpiresAt.Should().BeBefore(DateTime.UtcNow);
        });
    }

    [Fact]
    public void Heartbeat_DoesNotExtendAClosedSession()
    {
        WithDatabase(db =>
        {
            var service = new SessionService(db);
            var session = service.RegisterSession("task_life", "prn_life", "role_reader", "jti_life", 1, string.Empty);
            service.CloseSession(session.SessionId, "test");

            service.Heartbeat(session.SessionId).Should().BeNull();
            service.Heartbeat("ses_does_not_exist").Should().BeNull();
        });
    }

    [Fact]
    public void Heartbeat_StopsAtTheMaximumLifetimeAndNeverShortensTheExpiry()
    {
        WithDatabase(db =>
        {
            var options = new SessionLifetimeOptions { TtlMinutes = 60, MaxLifetimeMinutes = 120 };
            var service = new SessionService(db, options);
            var session = service.RegisterSession("task_life", "prn_life", "role_reader", "jti_life", 1, string.Empty);

            // Ten minutes of the maximum lifetime remain: the new expiry is capped there.
            var registeredAt = DateTime.UtcNow.AddMinutes(-110);
            db.Execute(
                "UPDATE container_sessions SET registered_at = @registeredAt, expires_at = @expiresAt WHERE session_id = @sid",
                new { registeredAt, expiresAt = DateTime.UtcNow.AddMinutes(5), sid = session.SessionId });

            var capped = service.Heartbeat(session.SessionId);
            capped.Should().NotBeNull();
            capped!.Value.Should().BeCloseTo(registeredAt.AddMinutes(120), TimeSpan.FromSeconds(1));

            // The maximum lifetime has passed: the session keeps its current expiry and is not extended.
            var currentExpiry = DateTime.UtcNow.AddMinutes(5);
            db.Execute(
                "UPDATE container_sessions SET registered_at = @registeredAt, expires_at = @expiresAt WHERE session_id = @sid",
                new { registeredAt = DateTime.UtcNow.AddMinutes(-121), expiresAt = currentExpiry, sid = session.SessionId });

            var unchanged = service.Heartbeat(session.SessionId);
            unchanged.Should().NotBeNull();
            unchanged!.Value.Should().BeCloseTo(currentExpiry, TimeSpan.FromSeconds(1));
        });
    }

    [Fact]
    public void ExtendSessionGrants_ExtendsOnlyTheSessionsActiveUnexpiredGrants()
    {
        WithDatabase(db =>
        {
            var catalog = new CapabilityCatalog(db);
            var soon = DateTime.UtcNow.AddMinutes(5);
            var active = catalog.CreateGrant("task_life", "ses_life", "prn_life", "file.read", "{}", -1, soon);
            var expired = catalog.CreateGrant("task_life", "ses_life", "prn_life", "file.list", "{}", -1, DateTime.UtcNow.AddMinutes(-1));
            var revoked = catalog.CreateGrant("task_life", "ses_life", "prn_life", "file.write", "{}", -1, soon);
            var otherSession = catalog.CreateGrant("task_life", "ses_other", "prn_life", "file.read", "{}", -1, soon);
            db.Execute(
                "UPDATE capability_grants SET status = @status WHERE grant_id = @gid",
                new { status = (int)GrantStatus.Revoked, gid = revoked.GrantId });

            var newExpiry = DateTime.UtcNow.AddMinutes(60);
            catalog.ExtendSessionGrants("ses_life", newExpiry).Should().Be(1);

            db.Get<CapabilityGrant>(active.GrantId)!.ExpiresAt.Should().BeCloseTo(newExpiry, TimeSpan.FromSeconds(1));
            db.Get<CapabilityGrant>(expired.GrantId)!.ExpiresAt.Should().BeBefore(DateTime.UtcNow);
            db.Get<CapabilityGrant>(revoked.GrantId)!.ExpiresAt.Should().BeCloseTo(soon, TimeSpan.FromSeconds(1));
            db.Get<CapabilityGrant>(otherSession.GrantId)!.ExpiresAt.Should().BeCloseTo(soon, TimeSpan.FromSeconds(1));
        });
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-5, 60)]
    [InlineData(120, 1440)]
    [InlineData(240, 1440)]
    [InlineData(60, 30)]
    public void LifetimeOptions_RejectUnsafeValues(int ttlMinutes, int maxLifetimeMinutes)
    {
        var options = new SessionLifetimeOptions { TtlMinutes = ttlMinutes, MaxLifetimeMinutes = maxLifetimeMinutes };

        var validate = () => options.Validate();

        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LifetimeOptions_DefaultTtlIsShorterThanTheSessionKeyCacheLifetime()
    {
        var options = new SessionLifetimeOptions();

        options.Invoking(o => o.Validate()).Should().NotThrow();
        options.Ttl.Should().BeLessThan(CacheSessionKeyStore.DefaultTtl);
        options.MaxLifetime.Should().BeGreaterThanOrEqualTo(options.Ttl);
    }

    [Fact]
    public void Initializer_AddsTheRevocationTargetIndexToAnExistingDatabase()
    {
        WithDatabase(db =>
        {
            // An existing database created before the index existed.
            db.EnsureTable<Revocation>();
            ListIndexes(db, "revocations").Should().NotContain("idx_revocations_target");

            new BrokerDbInitializer(db).Initialize();

            ListIndexes(db, "revocations").Should().Contain("idx_revocations_target");
            ListIndexes(db, "capability_grants").Should().Contain("idx_capability_grants_session");
        }, initialize: false);
    }

    private static IReadOnlyList<string> ListIndexes(BrokerDb db, string table)
        => db.Query<IndexInfo>($"PRAGMA index_list({table})").Select(index => index.Name).ToArray();

    private static void SetExpiry(BrokerDb db, string sessionId, DateTime expiresAt)
        => db.Execute(
            "UPDATE container_sessions SET expires_at = @expiresAt WHERE session_id = @sid",
            new { expiresAt, sid = sessionId });

    private static void WithDatabase(Action<BrokerDb> test, bool initialize = true)
    {
        var path = Path.Combine(Path.GetTempPath(), $"broker_session_life_{Guid.NewGuid():N}.db");
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

    private sealed class IndexInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
