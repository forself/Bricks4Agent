using System.Net;
using System.Text.Json.Nodes;
using Broker.Middleware;
using BrokerCore.Models;
using BrokerCore.Services;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Api;

/// <summary>
/// Events that land in the middle of a register or heartbeat request, placed at an exact point with
/// <see cref="ServiceCallHooks"/> instead of relying on timing. The expected behaviour:
///   - a heartbeat never turns a token of an earlier epoch into one of the current epoch: when the kill switch
///     lands after authentication the heartbeat is refused with the kill switch response, and when it lands
///     while the token is being renewed the renewed token carries the presented token's epoch, so the next
///     request is refused;
///   - a credential revoked while a registration that already passed verification is being stored leaves no
///     usable session behind, and the registration is answered with the usual rejection;
///   - a heartbeat does not renew a session whose registration credential has been revoked (an expired
///     credential only stops new registrations).
/// This class has a broker of its own (<see cref="HookedBrokerFixture"/>), so advancing the epoch here does not
/// affect other test classes.
/// </summary>
public sealed class SessionLifecycleRaceTests : IClassFixture<HookedBrokerFixture>
{
    private const string ReaderRole = "role_reader";
    private const string LoopbackAddress = "127.0.0.1";
    private const string HeartbeatPath = "/api/v1/sessions/heartbeat";
    private const string GrantsListPath = "/api/v1/grants/list";
    private const string ToolSpecListPath = "/api/v1/tool-specs/list";

    private readonly HookedBrokerFixture _fixture;
    private readonly EncryptedBrokerClient _client;

    public SessionLifecycleRaceTests(HookedBrokerFixture fixture)
    {
        _fixture = fixture;
        _client = new EncryptedBrokerClient(fixture.Client, fixture.Services);
    }

    // ── Kill switch during a heartbeat ─────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_RenewedTokenKeepsTheEpochOfThePresentedToken()
    {
        var session = await RegisterReaderAsync();

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "the heartbeat response was {0}", heartbeat);
        var renewed = BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token");
        renewed.Should().NotBeNullOrEmpty("the heartbeat response was {0}", heartbeat);
        EpochOf(renewed!).Should().Be(EpochOf(session.ScopedToken));
    }

    [Fact]
    public async Task Heartbeat_WhenTheKillSwitchLandsWhileTheTokenIsRenewed_IssuesATokenTheNextRequestRefuses()
    {
        var session = await RegisterReaderAsync();
        var presentedEpoch = EpochOf(session.ScopedToken);

        // The kill switch lands after authentication and after the heartbeat's own epoch check,
        // while the session is being extended and just before the new token is signed.
        using var killSwitch = _fixture.Hooks.Arm<ISessionService>(
            nameof(ISessionService.Heartbeat),
            args => Equals(args[0], session.SessionId),
            () => KillSwitch());

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        killSwitch.Fired.Should().BeTrue("the kill switch must land inside the heartbeat request");
        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "the heartbeat response was {0}", heartbeat);
        var renewed = BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token");
        renewed.Should().NotBeNullOrEmpty("the heartbeat response was {0}", heartbeat);
        EpochOf(renewed!).Should().Be(presentedEpoch, "a renewed token keeps the epoch of the token it replaces");
        _fixture.CurrentEpoch().Should().BeGreaterThan(presentedEpoch);

        var withRenewed = await _client.SendEncryptedAsync(session, GrantsListPath, payload: null, renewed);
        var nextHeartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, renewed);
        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: renewed);

        withRenewed.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the renewed token must not outlive the kill switch; the response was {0}", withRenewed);
        withRenewed.Message.Should().Be(BrokerAuthMiddleware.EpochAdvancedMessage, "the response was {0}", withRenewed);
        nextHeartbeat.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", nextHeartbeat);
        nextHeartbeat.Message.Should().Be(BrokerAuthMiddleware.EpochAdvancedMessage, "the response was {0}", nextHeartbeat);
        BrokerJson.ReadString(nextHeartbeat.Body, "data", "scoped_token").Should().BeNull();
        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", bearer);
    }

    [Fact]
    public async Task Heartbeat_WhenTheKillSwitchLandsAfterAuthentication_IsRefusedWithTheKillSwitchResponse()
    {
        var session = await RegisterReaderAsync();

        // BrokerAuth looks the session up right after its own epoch check: the kill switch lands in between.
        using var killSwitch = _fixture.Hooks.Arm<ISessionService>(
            nameof(ISessionService.GetSession),
            args => Equals(args[0], session.SessionId),
            () => KillSwitch());

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        killSwitch.Fired.Should().BeTrue("the kill switch must land inside the heartbeat request");
        heartbeat.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the heartbeat response was {0}", heartbeat);
        heartbeat.Message.Should().Be(BrokerAuthMiddleware.EpochAdvancedMessage, "the agent stops on this response; the response was {0}", heartbeat);
        BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token").Should().BeNull("no token may be issued; the response was {0}", heartbeat);
    }

    // ── Credential revoked during registration ─────────────────────────────────────────

    [Fact]
    public async Task Register_WhenTheCredentialIsRevokedAfterVerification_LeavesNoUsableSession()
    {
        var (principalId, taskId) = SeedReaderTask();
        var issued = _fixture.IssueRegistrationCredential(principalId, taskId);
        var credentials = _fixture.Services.GetRequiredService<IRegistrationCredentialService>();
        var sessions = _fixture.Services.GetRequiredService<ISessionService>();
        var sessionsEndedByRevoke = -1;

        // The registration has passed verification and is about to store its session when an administrator
        // revokes the credential: the credential is marked first, then its sessions are ended (none exists yet).
        using var revoke = _fixture.Hooks.Arm<ISessionService>(
            nameof(ISessionService.RegisterSession),
            args => Equals(args[1], principalId),
            () =>
            {
                credentials.Revoke(issued.CredentialId, "revoked during registration", "integration-test").Should().BeTrue();
                sessionsEndedByRevoke = sessions.RevokeSessionsByCredential(issued.CredentialId, "revoked during registration", "integration-test").Count;
            });

        var result = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress, registrationSecret: issued.Secret);

        revoke.Fired.Should().BeTrue("the revocation must land inside the register request");
        sessionsEndedByRevoke.Should().Be(0, "the session did not exist yet when the revocation looked for it");
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the register response was {0}", result);
        result.Session.Should().BeNull("no scoped token may be issued; the register response was {0}", result);
        WithoutTraceId(result.Body).Should().Be(await ReferenceRejectionAsync(), "the rejection must look like every other one; the register response was {0}", result);
        result.Body.Should().NotContain(issued.Secret);

        SessionsOf(principalId, taskId).Should().ContainSingle("the registration stored one session before it saw the revocation")
            .Which.Status.Should().Be(SessionStatus.Revoked, "no session of the revoked credential may stay active");
        (await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress, registrationSecret: issued.Secret))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the revoked credential cannot register again");
    }

    [Fact]
    public async Task Heartbeat_OfASessionWhoseCredentialWasRevoked_IsRefusedAndEndsTheSession()
    {
        var (principalId, taskId) = SeedReaderTask();
        var issued = _fixture.IssueRegistrationCredential(principalId, taskId);
        var result = await _client.RegisterAsync(principalId, taskId, registrationSecret: issued.Secret);
        result.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", result);
        var session = result.Session!;

        // Only the credential is marked revoked; its sessions are not ended here, as for a session stored
        // after a revocation had already looked for them. The heartbeat checks the credential itself.
        _fixture.Services.GetRequiredService<IRegistrationCredentialService>()
            .Revoke(issued.CredentialId, "credential only", "integration-test").Should().BeTrue();

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);
        var bearer = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the heartbeat response was {0}", heartbeat);
        heartbeat.Message.Should().Be("Session can no longer be renewed.", "the heartbeat response was {0}", heartbeat);
        BrokerJson.ReadString(heartbeat.Body, "data", "scoped_token").Should().BeNull();
        _fixture.FindSession(session.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the session's token must stop working; the response was {0}", bearer);
    }

    [Fact]
    public async Task Heartbeat_AfterTheCredentialExpired_StillRenewsTheSession()
    {
        var (principalId, taskId) = SeedReaderTask();
        var issued = _fixture.IssueRegistrationCredential(principalId, taskId);
        var result = await _client.RegisterAsync(principalId, taskId, registrationSecret: issued.Secret);
        result.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", result);
        var session = result.Session!;

        // Expiry only stops new registrations; it does not end a session registered while the credential was valid.
        _fixture.Db.Execute(
            "UPDATE registration_credentials SET expires_at = @past WHERE credential_id = @credentialId",
            new { past = DateTime.UtcNow.AddMinutes(-1), credentialId = issued.CredentialId });

        var heartbeat = await _client.SendEncryptedAsync(session, HeartbeatPath, payload: null, session.ScopedToken);

        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "the heartbeat response was {0}", heartbeat);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private async Task<BrokerTestSession> RegisterReaderAsync()
    {
        var (principalId, taskId) = SeedReaderTask();
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);
        var result = await _client.RegisterAsync(principalId, taskId, registrationSecret: secret);
        result.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", result);
        return result.Session!;
    }

    private (string PrincipalId, string TaskId) SeedReaderTask()
    {
        var principalId = EncryptedBrokerClient.NewId("prn_authz_race");
        var taskId = EncryptedBrokerClient.NewId("task_authz_race");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: ReaderRole);
        return (principalId, taskId);
    }

    private void KillSwitch()
        => _fixture.Services.GetRequiredService<IRevocationService>().IncrementEpoch("integration-test", "kill switch during a request");

    private int EpochOf(string token)
    {
        var claims = _fixture.Services.GetRequiredService<IScopedTokenService>().ValidateToken(token);
        claims.Should().NotBeNull("the token must carry a valid signature");
        return claims!.Epoch;
    }

    private IReadOnlyList<ContainerSession> SessionsOf(string principalId, string taskId)
        => _fixture.Db.Query<ContainerSession>(
            "SELECT * FROM container_sessions WHERE principal_id = @principalId AND task_id = @taskId",
            new { principalId, taskId });

    /// <summary>The body (without its trace id) of a rejection whose cause is plainly a missing credential.</summary>
    private async Task<string> ReferenceRejectionAsync()
    {
        var (principalId, taskId) = SeedReaderTask();
        var reference = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress);
        reference.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the reference response was {0}", reference);
        return WithoutTraceId(reference.Body);
    }

    private static string WithoutTraceId(string body)
    {
        if (JsonNode.Parse(body) is not JsonObject root)
        {
            return body;
        }

        foreach (var key in root.Select(pair => pair.Key).Where(key => key.Contains("trace", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            root.Remove(key);
        }

        return root.ToJsonString();
    }
}
