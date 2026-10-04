using System.Globalization;
using System.Net;
using System.Text.Json;
using BrokerCore.Models;
using BrokerCore.Services;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Api;

/// <summary>
/// HTTP-level tests for binding scoped tokens to their session and for token renewal on heartbeat.
/// The expected behaviour:
///   - a handshake envelope is accepted only by session registration;
///   - a token is accepted only in its own session: over that session's encrypted channel, or on the plain
///     (Bearer) path while the session is active and unexpired and matches the token's principal, task and role;
///   - closing, revoking, cancelling the task or expiry rejects every token of the session at once, Bearer included;
///   - heartbeat issues a new token for the same session (the previous one stays valid until it expires),
///     extends the session and its grants up to the maximum lifetime, never revives an expired session,
///     and refuses to renew once the principal or the task is no longer active.
/// </summary>
public sealed class SessionBindingTests : IClassFixture<BrokerAuthorizationFixture>
{
    private const string ReaderRole = "role_reader";
    private const string AdminRole = "role_admin";

    private const string HeartbeatPath = "/api/v1/sessions/heartbeat";
    private const string GrantsListPath = "/api/v1/grants/list";
    private const string ToolSpecListPath = "/api/v1/tool-specs/list";

    private readonly BrokerAuthorizationFixture _fixture;
    private readonly EncryptedBrokerClient _client;

    public SessionBindingTests(BrokerAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _client = new EncryptedBrokerClient(fixture.Client, fixture.Services);
    }

    // ── Handshake envelopes ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(GrantsListPath)]
    [InlineData("/api/v1/agents/list")]
    [InlineData(HeartbeatPath)]
    public async Task HandshakeEnvelope_ToAnEndpointOtherThanRegister_IsRejected(string path)
    {
        var admin = _client.OpenSession(AdminRole);

        var reply = await _client.SendHandshakeEnvelopeAsync(path, payload: null, admin.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the response was {0}", reply);
        reply.Message.Should().Be("Handshake envelopes are only accepted for session registration.", "the response was {0}", reply);
        reply.Body.Should().NotContain("\"data\":{", "no result may be returned; the response was {0}", reply);
        reply.Body.Should().NotContain("\"data\":[", "no result may be returned; the response was {0}", reply);
    }

    // ── Channel binding ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_PresentedOverAnotherActiveSessionsChannel_IsUnauthorized()
    {
        var first = _client.OpenSession(AdminRole);
        var second = _client.OpenSession(AdminRole);

        var crossChannel = await _client.SendEncryptedAsync(second, "/api/v1/agents/list", payload: null, first.ScopedToken);
        var ownChannel = await _client.SendEncryptedAsync(first, "/api/v1/agents/list", payload: null, first.ScopedToken);
        var secondOwn = await _client.SendEncryptedAsync(second, "/api/v1/agents/list", payload: null, second.ScopedToken);

        crossChannel.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the cross-channel response was {0}", crossChannel);
        crossChannel.Message.Should().Be("Token does not belong to this session.", "the cross-channel response was {0}", crossChannel);
        ownChannel.StatusCode.Should().Be(HttpStatusCode.OK, "the own-channel response was {0}", ownChannel);
        secondOwn.StatusCode.Should().Be(HttpStatusCode.OK, "the second session's own response was {0}", secondOwn);
    }

    [Fact]
    public async Task Token_WhoseClaimsDoNotMatchItsSession_IsUnauthorized()
    {
        var session = _client.OpenSession(ReaderRole);
        var forged = MintToken(session, principalId: EncryptedBrokerClient.NewId("prn_authz_other"));

        var encrypted = await _client.SendEncryptedAsync(session, GrantsListPath, payload: null, forged);
        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: forged);

        encrypted.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the encrypted response was {0}", encrypted);
        encrypted.Message.Should().Be("Token does not match its session.", "the encrypted response was {0}", encrypted);
        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
    }

    [Fact]
    public async Task Token_ForASessionThatDoesNotExist_IsUnauthorized()
    {
        var session = _client.OpenSession(ReaderRole);
        var orphan = MintToken(session, sessionId: EncryptedBrokerClient.NewId("ses_authz_missing"));

        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: orphan);

        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
        bearer.Message.Should().Be("Session is not active.", "the Bearer response was {0}", bearer);
    }

    // ── Session state applies to Bearer tokens too ─────────────────────────────────────

    [Fact]
    public async Task AdminRevoke_RejectsTheSessionsTokenOnItsChannelAndAsBearer()
    {
        var admin = _client.OpenSession(AdminRole);
        var target = _client.OpenSession(ReaderRole);

        var before = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: target.ScopedToken);
        var revoke = await _client.SendEncryptedAsync(
            admin,
            "/api/v1/admin/revoke",
            new { target_type = "session", target_id = target.SessionId, reason = "session binding test" },
            admin.ScopedToken);
        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: target.ScopedToken);
        var channel = await _client.SendEncryptedAsync(target, GrantsListPath, payload: null, target.ScopedToken);

        before.StatusCode.Should().Be(HttpStatusCode.OK, "the response before revocation was {0}", before);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, "the revoke response was {0}", revoke);
        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
        channel.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the channel response was {0}", channel);
    }

    [Fact]
    public async Task TaskCancel_RejectsTheSessionsBearerTokenImmediately()
    {
        var admin = _client.OpenSession(AdminRole);
        var session = _fixture.OpenSeededSession(_client, ReaderRole);

        var before = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: session.ScopedToken);
        var cancel = await _client.SendEncryptedAsync(
            admin,
            "/api/v1/tasks/cancel",
            new { task_id = session.TaskId, reason = "session binding test" },
            admin.ScopedToken);
        var after = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: session.ScopedToken);

        before.StatusCode.Should().Be(HttpStatusCode.OK, "the response before cancel was {0}", before);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, "the cancel response was {0}", cancel);
        // Cancelling the task only changes the session state (nothing is added to the revocation list),
        // so this is the session check at work.
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response after cancel was {0}", after);
        after.Message.Should().Be("Session is not active.", "the response after cancel was {0}", after);
    }

    [Fact]
    public async Task ExpiredSession_RejectsItsTokenEvenBeforeTheTokenExpires()
    {
        var session = _fixture.OpenSeededSession(_client, ReaderRole);
        SetSessionExpiry(session.SessionId, DateTime.UtcNow.AddMinutes(-1));

        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: session.ScopedToken);
        var channel = await _client.SendEncryptedAsync(session, GrantsListPath, payload: null, session.ScopedToken);

        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
        bearer.Message.Should().Be("Session expired.", "the Bearer response was {0}", bearer);
        channel.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the channel response was {0}", channel);
        channel.Message.Should().Be("Session expired.", "the channel response was {0}", channel);
    }

    // ── Heartbeat: token renewal ───────────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_IssuesANewTokenForTheSameSessionAndExtendsTheSessionAndItsGrants()
    {
        var session = await RegisterReaderAsync();
        var shortExpiry = DateTime.UtcNow.AddMinutes(5);
        SetSessionExpiry(session.SessionId, shortExpiry);
        SetGrantExpiry(session.SessionId, shortExpiry);
        var lifetime = _fixture.Services.GetRequiredService<SessionLifetimeOptions>();

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "the heartbeat response was {0}", heartbeat);
        BrokerJson.ReadString(heartbeat.Body, "data", "session_id").Should().Be(session.SessionId);
        var renewed = BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token");
        renewed.Should().NotBeNullOrEmpty("the heartbeat must return a token; the response was {0}", heartbeat);
        renewed.Should().NotBe(session.ScopedToken);
        ReadTime(heartbeat.Body, "token_expires_at").Should().BeAfter(DateTime.UtcNow);

        var tokens = _fixture.Services.GetRequiredService<IScopedTokenService>();
        var oldClaims = tokens.ValidateToken(session.ScopedToken)!;
        var newClaims = tokens.ValidateToken(renewed!)!;
        newClaims.Jti.Should().NotBe(oldClaims.Jti);
        newClaims.SessionId.Should().Be(session.SessionId);
        newClaims.PrincipalId.Should().Be(oldClaims.PrincipalId);
        newClaims.TaskId.Should().Be(oldClaims.TaskId);
        newClaims.RoleId.Should().Be(oldClaims.RoleId);

        var stored = _fixture.FindSession(session.SessionId)!;
        var expectedExpiry = DateTime.UtcNow + lifetime.Ttl;
        stored.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));
        stored.TokenJti.Should().Be(newClaims.Jti, "the session records its newest token");
        ReadTime(heartbeat.Body, "session_expires_at").Should().BeCloseTo(stored.ExpiresAt, TimeSpan.FromSeconds(1));

        var grants = FindGrants(session.SessionId);
        grants.Should().NotBeEmpty("registration grants the reader role's default capabilities");
        grants.Should().OnlyContain(grant => Math.Abs((grant.ExpiresAt - stored.ExpiresAt).TotalSeconds) < 1,
            "the grants follow the session's new expiry");

        var withNewToken = await _client.SendEncryptedAsync(session, GrantsListPath, payload: null, renewed);
        var withOldToken = await _client.SendEncryptedAsync(session, GrantsListPath, payload: null, session.ScopedToken);

        withNewToken.StatusCode.Should().Be(HttpStatusCode.OK, "the renewed token must work; the response was {0}", withNewToken);
        CountDataItems(withNewToken.Body).Should().BeGreaterThan(0, "the extended grants stay active; the response was {0}", withNewToken);
        withOldToken.StatusCode.Should().Be(HttpStatusCode.OK, "the previous token stays valid until it expires; the response was {0}", withOldToken);
    }

    [Fact]
    public async Task Heartbeat_DoesNotReviveAnExpiredSession()
    {
        var session = _fixture.OpenSeededSession(_client, ReaderRole);
        var past = DateTime.UtcNow.AddMinutes(-1);
        SetSessionExpiry(session.SessionId, past);

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the heartbeat response was {0}", heartbeat);
        BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token").Should().BeNull();
        _fixture.FindSession(session.SessionId)!.ExpiresAt.Should().BeBefore(DateTime.UtcNow, "an expired session stays expired");
    }

    [Fact]
    public async Task Heartbeat_ExtendsNoFurtherThanTheMaximumLifetime()
    {
        var session = _fixture.OpenSeededSession(_client, ReaderRole);
        var lifetime = _fixture.Services.GetRequiredService<SessionLifetimeOptions>();
        // Registered long enough ago that only ten minutes of its maximum lifetime remain.
        var registeredAt = DateTime.UtcNow - lifetime.MaxLifetime + TimeSpan.FromMinutes(10);
        _fixture.Db.Execute(
            "UPDATE container_sessions SET registered_at = @registeredAt, expires_at = @expiresAt WHERE session_id = @sid",
            new { registeredAt, expiresAt = DateTime.UtcNow.AddMinutes(5), sid = session.SessionId });

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "the heartbeat response was {0}", heartbeat);
        var stored = _fixture.FindSession(session.SessionId)!;
        stored.ExpiresAt.Should().BeCloseTo(registeredAt + lifetime.MaxLifetime, TimeSpan.FromSeconds(1));
        stored.ExpiresAt.Should().BeBefore(DateTime.UtcNow + lifetime.Ttl - TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Heartbeat_AfterTheTaskEndedOrThePrincipalWasDisabled_IsRefusedAndEndsTheSession(bool endTask)
    {
        var session = _fixture.OpenSeededSession(_client, ReaderRole);
        if (endTask)
        {
            _fixture.Db.Execute(
                "UPDATE broker_tasks SET state = @state WHERE task_id = @taskId",
                new { state = (int)TaskState.Completed, taskId = session.TaskId });
        }
        else
        {
            _fixture.Db.Execute(
                "UPDATE principals SET status = @status WHERE principal_id = @principalId",
                new { status = (int)EntityStatus.Disabled, principalId = session.PrincipalId });
        }

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);
        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the heartbeat response was {0}", heartbeat);
        heartbeat.Message.Should().Be("Session can no longer be renewed.", "the heartbeat response was {0}", heartbeat);
        BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token").Should().BeNull();
        _fixture.FindSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
    }

    [Fact]
    public async Task SessionClose_AfterRenewal_RejectsTheEarlierTokenToo()
    {
        var session = _fixture.OpenSeededSession(_client, ReaderRole);
        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);
        var renewed = BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token");
        renewed.Should().NotBeNullOrEmpty("the heartbeat response was {0}", heartbeat);

        var close = await _client.SendEncryptedAsync(session, "/api/v1/sessions/close", new { reason = "session binding test" }, renewed);
        var earlier = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: session.ScopedToken);
        var latest = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: renewed);

        close.StatusCode.Should().Be(HttpStatusCode.OK, "the close response was {0}", close);
        // The earlier token is not on the revocation list; the closed session rejects it.
        earlier.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the earlier token's response was {0}", earlier);
        earlier.Message.Should().Be("Session is not active.", "the earlier token's response was {0}", earlier);
        latest.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the latest token's response was {0}", latest);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>Registers a reader for a seeded task through the real handshake, so the session has grants.</summary>
    private async Task<BrokerTestSession> RegisterReaderAsync()
    {
        var principalId = EncryptedBrokerClient.NewId("prn_authz_binding");
        var taskId = EncryptedBrokerClient.NewId("task_authz_binding");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: ReaderRole);

        var result = await _client.RegisterAsync(principalId, taskId);
        result.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", result);
        result.Session.Should().NotBeNull("the register response was {0}", result);
        BrokerJson.ReadString(result.Body, "data", "token_expires_at").Should().NotBeNullOrEmpty(
            "registration reports when the token expires; the register response was {0}", result);
        return result.Session!;
    }

    private string MintToken(BrokerTestSession session, string? principalId = null, string? sessionId = null)
        => _fixture.Services.GetRequiredService<IScopedTokenService>().GenerateToken(new ScopedTokenClaims
        {
            PrincipalId = principalId ?? session.PrincipalId,
            Jti = BrokerCore.IdGen.New("jti"),
            TaskId = session.TaskId,
            SessionId = sessionId ?? session.SessionId,
            RoleId = session.RoleId,
            Epoch = _fixture.CurrentEpoch()
        });

    private void SetSessionExpiry(string sessionId, DateTime expiresAt)
        => _fixture.Db.Execute(
            "UPDATE container_sessions SET expires_at = @expiresAt WHERE session_id = @sid",
            new { expiresAt, sid = sessionId });

    private void SetGrantExpiry(string sessionId, DateTime expiresAt)
        => _fixture.Db.Execute(
            "UPDATE capability_grants SET expires_at = @expiresAt WHERE session_id = @sid",
            new { expiresAt, sid = sessionId });

    private IReadOnlyList<CapabilityGrant> FindGrants(string sessionId)
        => _fixture.Db.Query<CapabilityGrant>(
            "SELECT * FROM capability_grants WHERE session_id = @sid",
            new { sid = sessionId });

    private static DateTime ReadTime(string body, string field)
    {
        var text = BrokerJson.ReadString(body, "data", field);
        text.Should().NotBeNullOrEmpty("the response must carry {0}; the response was {1}", field, body);
        return DateTimeOffset.Parse(text!, CultureInfo.InvariantCulture).UtcDateTime;
    }

    private static int CountDataItems(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
    }
}
