using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;
using BrokerCore.Models;
using Integration.Tests.Fixtures;

namespace Integration.Tests.Api;

/// <summary>
/// HTTP-level regression tests for authentication and authorization on the broker's /api/v1 surface.
/// Every request goes through the real middleware pipeline of a dedicated host
/// (<see cref="BrokerAuthorizationFixture"/>); broker services are used only to mint sessions and
/// scoped tokens and to seed principals, tasks, plans, execution requests and shared-context entries.
/// The expected behaviour:
///   - an /api/v1 endpoint requires a valid scoped token unless it is public or authenticated by its
///     own mechanism (signed link, local-admin cookie, portal session, worker signature), whatever the
///     HTTP method casing or a trailing slash;
///   - management endpoints require the admin role; task endpoints (tasks, plans, shared context,
///     execution-request query) require the task owner or an admin;
///   - shared-context reads and writes stay inside tasks the caller may access (lookups by key default to
///     the caller's task, new documents land in it); the system scope and the document ids the broker
///     maintains itself are admin-only, and versions other principals left under those ids are ignored;
///     a malformed ACL is refused on write and grants nothing on read;
///   - session registration issues tokens only for tasks with an assigned principal and role, always with
///     the task's role, and administrative roles only for callers on the local host (the registration
///     credential itself is covered by <see cref="RegistrationCredentialTests"/>);
///   - closing a session invalidates its token, on its own channel and as a Bearer token alike
///     (token/session binding and renewal are covered by <see cref="SessionBindingTests"/>);
///   - the portal lists only the signed-in user's own artifacts;
///   - the public, signed-link, local-admin and worker-signed flows keep working.
/// </summary>
public sealed class BrokerAuthorizationRegressionTests : IClassFixture<BrokerAuthorizationFixture>
{
    private const string ReaderRole = "role_reader";
    private const string ExecutorRole = "role_executor";
    private const string AdminRole = "role_admin";

    // The shared-context scope the broker uses for its own documents; it has no task.
    private const string SystemTaskId = "global";
    private const string OpenAcl = "{\"read\":[\"*\"]}";

    // 203.0.113.0/24 is a documentation range (TEST-NET-3): a caller address that is not loopback.
    private const string NonLoopbackAddress = "203.0.113.7";
    private const string LoopbackAddress = "127.0.0.1";

    private const string PortalPassword = "authz-regression-password";

    private readonly BrokerAuthorizationFixture _fixture;
    private readonly EncryptedBrokerClient _client;

    public BrokerAuthorizationRegressionTests(BrokerAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _client = new EncryptedBrokerClient(fixture.Client, fixture.Services);
    }

    // ── Default authentication on /api/v1 ──────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/high-level/line/users")]
    [InlineData("GET", "/api/v1/high-level/line/registration-policy")]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending")]
    [InlineData("GET", "/api/v1/high-level/line/users/")]
    [InlineData("GET", "/api/v1/high-level/line/registration-policy/")]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending/")]
    [InlineData("get", "/api/v1/high-level/line/users")]
    [InlineData("get", "/api/v1/high-level/line/notifications/pending")]
    public async Task ProtectedGet_WithoutCredentials_IsUnauthorized(string method, string path)
    {
        var reply = await _client.SendPlainAsync(method, path);

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", reply);
    }

    [Fact]
    public async Task TokenWithAlteredSignature_IsUnauthorized()
    {
        var admin = _client.OpenSession(AdminRole);
        var altered = AlterSignature(admin.ScopedToken);

        var bearer = await _client.SendPlainAsync("GET", "/api/v1/high-level/line/users", bearerToken: altered);
        var encrypted = await _client.SendEncryptedAsync(admin, "/api/v1/agents/list", payload: null, altered);

        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
        encrypted.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the encrypted response was {0}", encrypted);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    public async Task MalformedToken_IsUnauthorized(string malformed)
    {
        var admin = _client.OpenSession(AdminRole);

        var bearer = await _client.SendPlainAsync("GET", "/api/v1/high-level/line/users", bearerToken: malformed);
        var encrypted = await _client.SendEncryptedAsync(admin, "/api/v1/agents/list", payload: null, malformed);

        bearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Bearer response was {0}", bearer);
        encrypted.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the encrypted response was {0}", encrypted);
    }

    [Theory]
    [InlineData("/api/v1/workers")]
    [InlineData("/api/v1/workers/")]
    [InlineData("/api/v1/workers/containers")]
    [InlineData("/api/v1/workers/health")]
    [InlineData("/api/v1/health/workers")]
    [InlineData("/api/v1/health/score")]
    [InlineData("/api/v1/health/score/history")]
    public async Task WorkerPoolGet_WithoutCredentials_ReturnsNoResult(string path)
    {
        var reply = await _client.SendPlainAsync("GET", path);

        // These routes are mapped only when the function pool is enabled, which this host does not do;
        // mapped or not, an anonymous caller must not receive a result.
        reply.Status.Should().BeOneOf(new[] { 401, 403, 404 }, "the response was {0}", reply);
    }

    [Theory]
    [InlineData("POST", "/api/v1/tasks/create")]
    [InlineData("POST", "/api/v1/tasks/query")]
    [InlineData("POST", "/api/v1/agents/list")]
    [InlineData("POST", "/api/v1/admin/epoch/query")]
    [InlineData("POST", "/api/v1/capabilities/list")]
    [InlineData("post", "/api/v1/tasks/create")]
    [InlineData("post", "/api/v1/agents/list")]
    [InlineData("post", "/api/v1/capabilities/list")]
    [InlineData("Post", "/api/v1/admin/epoch/query")]
    public async Task EncryptedPost_OverValidSessionWithoutToken_IsUnauthorized(string method, string path)
    {
        // A valid encrypted session but no scoped token anywhere: the rejection must come from
        // authentication, not from the envelope layer.
        var channel = _client.OpenSession(ReaderRole);

        var reply = await _client.SendEncryptedAsync(
            channel,
            path,
            "{\"task_type\":\"query\",\"task_id\":\"task_authz_unknown\"}",
            scopedToken: null,
            method: method);

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", reply);
    }

    [Theory]
    [InlineData("POST", "/api/v1/high-level/line/process")]
    [InlineData("POST", "/api/v1/high-level/line/process/")]
    [InlineData("post", "/api/v1/high-level/line/process")]
    [InlineData("POST", "/api/v1/high-level/line/notifications/complete")]
    [InlineData("POST", "/api/v1/high-level/line/notifications/complete/")]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending")]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending/")]
    public async Task WorkerSignedRoute_WithScopedTokenButNoWorkerSignature_IsUnauthorized(string method, string path)
    {
        // Worker routes accept only a worker signature; even an admin scoped token is not a substitute.
        var admin = _client.OpenSession(AdminRole);
        var isGet = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
        var body = isGet
            ? null
            : JsonSerializer.Serialize(new
            {
                user_id = "authz-probe-user",
                message = "hello",
                notification_id = "ntf_authz_unknown",
                status = "sent"
            });

        var reply = await _client.SendPlainAsync(method, path, body, bearerToken: admin.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", reply);
    }

    // ── Admin-only endpoints ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("POST", "/api/v1/tasks/create", "{\"task_type\":\"query\",\"assigned_role_id\":\"role_admin\"}")]
    [InlineData("POST", "/api/v1/agents/create", "{\"task_type\":\"analysis\"}")]
    [InlineData("POST", "/api/v1/agents/list", "{}")]
    [InlineData("POST", "/api/v1/agents/capabilities", "{}")]
    [InlineData("POST", "/api/v1/audit/trace", "{\"trace_id\":\"trace_authz_probe\"}")]
    [InlineData("POST", "/api/v1/audit/verify", "{\"trace_id\":\"trace_authz_probe\"}")]
    [InlineData("POST", "/api/v1/admin/kill-switch", "{\"reason\":\"authorization regression\"}")]
    [InlineData("POST", "/api/v1/admin/registration-credentials/issue", "{\"principal_id\":\"prn_authz_probe\",\"task_id\":\"task_authz_probe\"}")]
    [InlineData("POST", "/api/v1/admin/registration-credentials/revoke", "{\"principal_id\":\"prn_authz_probe\",\"task_id\":\"task_authz_probe\"}")]
    [InlineData("POST", "/api/v1/admin/registration-credentials/list", "{}")]
    [InlineData("post", "/api/v1/tasks/create", "{\"task_type\":\"query\"}")]
    [InlineData("post", "/api/v1/agents/list", "{}")]
    public async Task AdminEndpoint_WithNonAdminToken_IsForbidden(string method, string path, string payload)
    {
        var epochBefore = _fixture.CurrentEpoch();
        var reader = _client.OpenSession(ReaderRole);

        var reply = await _client.SendEncryptedAsync(reader, path, payload, reader.ScopedToken, method);

        reply.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", reply);
        _fixture.CurrentEpoch().Should().Be(epochBefore, "a rejected request must not advance the system epoch");
    }

    [Theory]
    [InlineData("GET", "/api/v1/high-level/line/users", null)]
    [InlineData("GET", "/api/v1/high-level/line/registration-policy", null)]
    [InlineData("POST", "/api/v1/high-level/line/users/permissions", "{\"user_id\":\"authz-probe-user\"}")]
    [InlineData("POST", "/api/v1/high-level/line/users/registration/review", "{\"user_id\":\"authz-probe-user\",\"action\":\"approve\"}")]
    [InlineData("POST", "/api/v1/high-level/line/profile", "{\"user_id\":\"authz-probe-user\"}")]
    [InlineData("POST", "/api/v1/high-level/line/draft", "{\"user_id\":\"authz-probe-user\"}")]
    public async Task LineManagementRoute_WithNonAdminBearerToken_IsForbidden(string method, string path, string? body)
    {
        var reader = _client.OpenSession(ReaderRole);

        var reply = await _client.SendPlainAsync(method, path, body, bearerToken: reader.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", reply);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("post")]
    public async Task AgentList_WithAdminToken_Succeeds(string method)
    {
        var admin = _client.OpenSession(AdminRole);

        var reply = await _client.SendEncryptedAsync(admin, "/api/v1/agents/list", payload: null, admin.ScopedToken, method);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
        reply.SuccessFlag.Should().BeTrue("the response was {0}", reply);
    }

    [Fact]
    public async Task TaskCreate_WithAdminToken_Succeeds()
    {
        var admin = _client.OpenSession(AdminRole);

        var reply = await _client.SendEncryptedAsync(admin, "/api/v1/tasks/create", new { task_type = "query" }, admin.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
    }

    [Fact]
    public async Task LineManagementGets_WithAdminBearerToken_Succeed()
    {
        var admin = _client.OpenSession(AdminRole);

        var users = await _client.SendPlainAsync("GET", "/api/v1/high-level/line/users", bearerToken: admin.ScopedToken);
        var policy = await _client.SendPlainAsync("GET", "/api/v1/high-level/line/registration-policy", bearerToken: admin.ScopedToken);

        users.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", users);
        policy.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", policy);
    }

    // ── Task ownership ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TaskQueryAndCancel_ForAnotherPrincipalsTask_AreForbidden()
    {
        var owner = Id("prn_authz_owner");
        var otherPrincipal = Id("prn_authz_other");
        var ownTaskId = Id("task_authz_own");
        var otherTaskId = Id("task_authz_other");
        _fixture.SeedTask(ownTaskId, "query", submittedBy: "system", assignedPrincipalId: owner, assignedRoleId: ReaderRole);
        _fixture.SeedTask(otherTaskId, "query", submittedBy: otherPrincipal, assignedPrincipalId: otherPrincipal, assignedRoleId: ReaderRole);
        var session = _client.OpenSession(ReaderRole, owner, ownTaskId);

        var query = await _client.SendEncryptedAsync(session, "/api/v1/tasks/query", new { task_id = otherTaskId }, session.ScopedToken);
        var cancel = await _client.SendEncryptedAsync(
            session,
            "/api/v1/tasks/cancel",
            new { task_id = otherTaskId, reason = "authorization regression" },
            session.ScopedToken);

        query.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the query response was {0}", query);
        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the cancel response was {0}", cancel);
        _fixture.FindTask(otherTaskId)!.State.Should().Be(TaskState.Active, "a forbidden cancel must not change the task");
    }

    [Fact]
    public async Task TaskQueryAndCancel_ForOwnTasks_Succeed()
    {
        var owner = Id("prn_authz_owner");
        var tokenTaskId = Id("task_authz_token");
        var submittedTaskId = Id("task_authz_submitted");
        // The token's own task, and a separate task the same principal submitted.
        _fixture.SeedTask(tokenTaskId, "query", submittedBy: "system", assignedPrincipalId: owner, assignedRoleId: ReaderRole);
        _fixture.SeedTask(submittedTaskId, "query", submittedBy: owner, assignedPrincipalId: null, assignedRoleId: null);
        var session = _client.OpenSession(ReaderRole, owner, tokenTaskId);

        var query = await _client.SendEncryptedAsync(session, "/api/v1/tasks/query", new { task_id = tokenTaskId }, session.ScopedToken);
        var cancel = await _client.SendEncryptedAsync(
            session,
            "/api/v1/tasks/cancel",
            new { task_id = submittedTaskId, reason = "authorization regression" },
            session.ScopedToken);

        query.StatusCode.Should().Be(HttpStatusCode.OK, "the query response was {0}", query);
        query.SuccessFlag.Should().BeTrue("the query response was {0}", query);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, "the cancel response was {0}", cancel);
        _fixture.FindTask(submittedTaskId)!.State.Should().Be(TaskState.Cancelled);
    }

    [Fact]
    public async Task TaskQuery_ForAnyTask_IsAllowedForAdmin()
    {
        var otherPrincipal = Id("prn_authz_other");
        var taskId = Id("task_authz_any");
        _fixture.SeedTask(taskId, "query", submittedBy: otherPrincipal, assignedPrincipalId: otherPrincipal, assignedRoleId: ReaderRole);
        var admin = _client.OpenSession(AdminRole);

        var reply = await _client.SendEncryptedAsync(admin, "/api/v1/tasks/query", new { task_id = taskId }, admin.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
    }

    // ── Session registration (real ECDH handshake) ─────────────────────────────────────

    [Fact]
    public async Task Register_ForTaskWithoutAssignedPrincipal_IsRejected()
    {
        var principalId = Id("prn_authz_reg");
        var taskId = Id("task_authz_reg");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: null, assignedRoleId: ReaderRole);
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);

        var result = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress, registrationSecret: secret);

        AssertRegistrationRejected(result);
    }

    [Fact]
    public async Task Register_ForTaskWithoutAssignedRole_IsRejectedEvenWhenARoleIsRequested()
    {
        var principalId = Id("prn_authz_reg");
        var taskId = Id("task_authz_reg");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: null);
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);

        // Loopback caller with a valid credential, so only the missing task role can be the reason for the rejection.
        var result = await _client.RegisterAsync(principalId, taskId, roleId: AdminRole, remoteAddress: LoopbackAddress, registrationSecret: secret);

        AssertRegistrationRejected(result);
    }

    [Fact]
    public async Task Register_WithRequestedRoleDifferentFromTaskRole_ReportsTaskAssignedRoleMismatch()
    {
        var principalId = Id("prn_authz_reg");
        var taskId = Id("task_authz_reg");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: ReaderRole);
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);

        var result = await _client.RegisterAsync(principalId, taskId, roleId: ExecutorRole, registrationSecret: secret);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the register response was {0}", result);
        result.Body.Should().ContainEquivalentOf("task-assigned role", "the register response was {0}", result);
        result.Session.Should().BeNull("no scoped token may be issued; the register response was {0}", result);
    }

    [Theory]
    [InlineData(NonLoopbackAddress)]
    [InlineData(null)]
    public async Task Register_AdminRole_FromNonLoopbackOrUnknownAddress_IsRejected(string? remoteAddress)
    {
        var principalId = Id("prn_authz_reg");
        var taskId = Id("task_authz_reg");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: AdminRole);
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);

        var result = await _client.RegisterAsync(principalId, taskId, remoteAddress: remoteAddress, registrationSecret: secret);

        AssertRegistrationRejected(result);
        // The credential is valid, so the rejection is the local-host rule for administrative roles.
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the register response was {0}", result);
    }

    [Theory]
    [InlineData(LoopbackAddress)]
    [InlineData("::1")]
    public async Task Register_AdminRole_FromLoopback_IssuesAWorkingToken(string remoteAddress)
    {
        var principalId = Id("prn_authz_reg");
        var taskId = Id("task_authz_reg");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: AdminRole);
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);

        var result = await _client.RegisterAsync(principalId, taskId, remoteAddress: remoteAddress, registrationSecret: secret);

        result.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", result);
        result.Session.Should().NotBeNull("a scoped token must be issued; the register response was {0}", result);

        var session = result.Session!;
        var reply = await _client.SendEncryptedAsync(session, "/api/v1/agents/list", payload: null, session.ScopedToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the issued admin token must work; the response was {0}", reply);
    }

    [Fact]
    public async Task Register_NonAdminRole_FromNonLoopback_IssuesAWorkingToken()
    {
        var principalId = Id("prn_authz_reg");
        var taskId = Id("task_authz_reg");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: ReaderRole);
        var secret = _fixture.SeedRegistrationCredential(principalId, taskId);

        var result = await _client.RegisterAsync(principalId, taskId, remoteAddress: NonLoopbackAddress, registrationSecret: secret);

        result.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", result);
        result.Session.Should().NotBeNull("a scoped token must be issued; the register response was {0}", result);

        var session = result.Session!;
        var reply = await _client.SendEncryptedAsync(session, "/api/v1/grants/list", payload: null, session.ScopedToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the issued token must work; the response was {0}", reply);
    }

    // ── Session close ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionClose_InvalidatesTheSessionToken()
    {
        var closing = _client.OpenSession(ReaderRole);
        var other = _client.OpenSession(ReaderRole);

        var beforeClose = await _client.SendEncryptedAsync(closing, "/api/v1/admin/epoch/query", payload: null, closing.ScopedToken);
        var beforeCloseBearer = await _client.SendPlainAsync("POST", "/api/v1/tool-specs/list", "{}", bearerToken: closing.ScopedToken);
        var close = await _client.SendEncryptedAsync(
            closing,
            "/api/v1/sessions/close",
            new { reason = "authorization regression" },
            closing.ScopedToken);

        // A token is accepted only in its own session, so after close it is presented on the closed session's
        // own channel and on the plain path that carries no channel (Bearer).
        var afterCloseOwnChannel = await _client.SendEncryptedAsync(closing, "/api/v1/admin/epoch/query", payload: null, closing.ScopedToken);
        var afterCloseBearer = await _client.SendPlainAsync("POST", "/api/v1/tool-specs/list", "{}", bearerToken: closing.ScopedToken);
        var otherToken = await _client.SendEncryptedAsync(other, "/api/v1/admin/epoch/query", payload: null, other.ScopedToken);

        beforeClose.StatusCode.Should().Be(HttpStatusCode.OK, "the token works before close; the response was {0}", beforeClose);
        beforeCloseBearer.StatusCode.Should().Be(HttpStatusCode.OK, "the Bearer token works before close; the response was {0}", beforeCloseBearer);
        close.StatusCode.Should().Be(HttpStatusCode.OK, "the close response was {0}", close);
        afterCloseOwnChannel.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the closed session's channel must be rejected; the response was {0}", afterCloseOwnChannel);
        afterCloseBearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the token of a closed session must be rejected; the response was {0}", afterCloseBearer);
        otherToken.StatusCode.Should().Be(HttpStatusCode.OK, "other sessions are unaffected; the response was {0}", otherToken);
    }

    // ── Flows that must keep working ───────────────────────────────────────────────────

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Health_RemainsPublic(string method)
    {
        var reply = await _client.SendPlainAsync(method, "/api/v1/health");

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
        reply.Body.Should().Contain("broker_public_key");
    }

    [Fact]
    public async Task LocalAdminStatus_RemainsAvailableWithoutToken()
    {
        var reply = await _client.SendPlainAsync("GET", "/api/v1/local-admin/status");

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
    }

    [Fact]
    public async Task LineWorkerSignedRoutes_PassWorkerVerification()
    {
        var processBody = JsonSerializer.Serialize(new { user_id = Id("authz-line-user"), message = "hello" });
        var completeBody = JsonSerializer.Serialize(new { notification_id = Id("ntf_authz"), status = "sent" });

        var process = await _fixture.SendLineWorkerSignedAsync(HttpMethod.Post, "/api/v1/high-level/line/process", processBody);
        var pending = await _fixture.SendLineWorkerSignedAsync(HttpMethod.Get, "/api/v1/high-level/line/notifications/pending?limit=5");
        var complete = await _fixture.SendLineWorkerSignedAsync(HttpMethod.Post, "/api/v1/high-level/line/notifications/complete", completeBody);

        process.StatusCode.Should().Be(HttpStatusCode.OK, "the process response was {0}", process);
        pending.StatusCode.Should().Be(HttpStatusCode.OK, "the pending response was {0}", pending);
        // An unknown notification id is answered by the handler, i.e. after authentication.
        complete.StatusCode.Should().Be(HttpStatusCode.NotFound, "the complete response was {0}", complete);
    }

    [Fact]
    public async Task ArtifactDownload_IsCheckedByItsLinkSignatureNotByScopedToken()
    {
        var artifactId = Id("art_authz");
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

        var unsigned = await _client.SendPlainAsync("GET", $"/api/v1/artifacts/download/{artifactId}");
        var badSignature = await _client.SendPlainAsync("GET", $"/api/v1/artifacts/download/{artifactId}?exp={expiry}&sig=00");

        // 401 would mean the scoped-token check intercepted the request; the link handler answers 403/404/410.
        unsigned.Status.Should().BeOneOf(new[] { 403, 404, 410 }, "the unsigned response was {0}", unsigned);
        badSignature.Status.Should().BeOneOf(new[] { 403, 404, 410 }, "the bad-signature response was {0}", badSignature);
    }

    [Fact]
    public async Task UserApprovalList_IsCheckedByItsLinkToken()
    {
        var reply = await _client.SendPlainAsync("GET", "/api/v1/user/approvals?token=not-a-valid-link");

        // The handler's link check answers (its message names the link), not the scoped-token check.
        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", reply);
        reply.Message.Should().ContainEquivalentOf("link", "the response was {0}", reply);
    }

    [Theory]
    [InlineData("/api/v1/capabilities/list")]
    [InlineData("/api/v1/grants/list")]
    [InlineData("/api/v1/sessions/heartbeat")]
    [InlineData("/api/v1/admin/epoch/query")]
    public async Task AgentRuntimeEndpoint_WithNonAdminToken_Succeeds(string path)
    {
        // A session for a real principal and task: the heartbeat renews tokens only while both are still active.
        var reader = _fixture.OpenSeededSession(_client, ReaderRole);

        var reply = await _client.SendEncryptedAsync(reader, path, payload: null, reader.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
    }

    // ── tool-specs: plain JSON body (no envelope), any valid session ────────────────────

    [Theory]
    [InlineData("POST", "/api/v1/tool-specs/list")]
    [InlineData("post", "/api/v1/tool-specs/list")]
    [InlineData("POST", "/api/v1/tool-specs/list/")]
    [InlineData("POST", "/api/v1/tool-specs/get")]
    public async Task ToolSpecPost_WithoutToken_IsUnauthorized(string method, string path)
    {
        var reply = await _client.SendPlainAsync(method, path, "{\"tool_id\":\"authz-probe\"}");

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", reply);
        reply.Message.Should().Be("Missing authentication token.", "the response was {0}", reply);
    }

    [Fact]
    public async Task ToolSpecs_WithSessionTokenAndPlainJsonBody_Succeed()
    {
        var reader = _client.OpenSession(ReaderRole);

        var listWithBearer = await _client.SendPlainAsync(
            "POST", "/api/v1/tool-specs/list", "{}", bearerToken: reader.ScopedToken);
        var listWithBodyToken = await _client.SendPlainAsync(
            "POST", "/api/v1/tool-specs/list", JsonSerializer.Serialize(new { scoped_token = reader.ScopedToken }));
        var unknownTool = await _client.SendPlainAsync(
            "POST",
            "/api/v1/tool-specs/get",
            JsonSerializer.Serialize(new { tool_id = Id("authz_unknown_tool") }),
            bearerToken: reader.ScopedToken);

        listWithBearer.StatusCode.Should().Be(HttpStatusCode.OK, "the Bearer response was {0}", listWithBearer);
        listWithBearer.SuccessFlag.Should().BeTrue("the Bearer response was {0}", listWithBearer);
        listWithBodyToken.StatusCode.Should().Be(HttpStatusCode.OK, "the body-token response was {0}", listWithBodyToken);
        listWithBodyToken.SuccessFlag.Should().BeTrue("the body-token response was {0}", listWithBodyToken);
        // An unknown tool id is answered by the handler, i.e. after authentication.
        unknownTool.StatusCode.Should().Be(HttpStatusCode.NotFound, "the get response was {0}", unknownTool);
        unknownTool.Message.Should().Be("Tool spec not found.", "the get response was {0}", unknownTool);
    }

    // ── Task scope: plans/* ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreate_ForAnotherTask_IsForbidden()
    {
        var scope = OpenTaskScope();

        var reply = await _client.SendEncryptedAsync(
            scope.Session,
            "/api/v1/plans/create",
            new { task_id = scope.OtherTaskId, title = "authorization regression" },
            scope.Session.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", reply);
        _fixture.CountPlans(scope.OtherTaskId).Should().Be(0, "a forbidden create must not add a plan to the other task");
    }

    [Theory]
    [InlineData("/api/v1/plans/get")]
    [InlineData("/api/v1/plans/add-node")]
    [InlineData("/api/v1/plans/add-edge")]
    [InlineData("/api/v1/plans/validate")]
    [InlineData("/api/v1/plans/submit")]
    [InlineData("/api/v1/plans/status")]
    public async Task PlanEndpoint_ForAnotherTasksPlan_IsForbidden(string path)
    {
        var scope = OpenTaskScope();
        var plan = _fixture.CreatePlan(scope.OtherTaskId, scope.OtherPrincipalId);
        var payload = new
        {
            plan_id = plan.PlanId,
            capability_id = "file.read",
            intent = "authorization regression",
            from_node_id = Id("node_authz_from"),
            to_node_id = Id("node_authz_to")
        };

        var reply = await _client.SendEncryptedAsync(scope.Session, path, payload, scope.Session.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", reply);
        var after = _fixture.FindPlan(plan.PlanId)!;
        after.TotalNodes.Should().Be(0, "a forbidden request must not change the plan");
        after.State.Should().Be(PlanState.Draft, "a forbidden request must not change the plan");
    }

    [Fact]
    public async Task PlanEndpoints_ForOwnTask_Succeed()
    {
        var scope = OpenTaskScope();
        var session = scope.Session;

        var create = await _client.SendEncryptedAsync(
            session, "/api/v1/plans/create", new { task_id = scope.OwnTaskId, title = "authorization regression" }, session.ScopedToken);
        create.StatusCode.Should().Be(HttpStatusCode.OK, "the create response was {0}", create);
        var planId = BrokerJson.ReadString(create.Body, "data", "planId");
        planId.Should().NotBeNullOrEmpty("the create response was {0}", create);

        // A plan without nodes fails DAG validation inside the handler, i.e. after authorization,
        // so submit can be checked without executing anything.
        var emptySubmit = await _client.SendEncryptedAsync(session, "/api/v1/plans/submit", new { plan_id = planId }, session.ScopedToken);
        emptySubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the submit response was {0}", emptySubmit);
        emptySubmit.Message.Should().Contain("DAG validation failed", "the submit response was {0}", emptySubmit);

        var first = await _client.SendEncryptedAsync(
            session, "/api/v1/plans/add-node", new { plan_id = planId, capability_id = "file.read", intent = "first" }, session.ScopedToken);
        var second = await _client.SendEncryptedAsync(
            session, "/api/v1/plans/add-node", new { plan_id = planId, capability_id = "file.read", intent = "second" }, session.ScopedToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the add-node response was {0}", first);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "the add-node response was {0}", second);

        var edge = await _client.SendEncryptedAsync(
            session,
            "/api/v1/plans/add-edge",
            new
            {
                plan_id = planId,
                from_node_id = BrokerJson.ReadString(first.Body, "data", "nodeId"),
                to_node_id = BrokerJson.ReadString(second.Body, "data", "nodeId")
            },
            session.ScopedToken);
        var validate = await _client.SendEncryptedAsync(session, "/api/v1/plans/validate", new { plan_id = planId }, session.ScopedToken);
        var get = await _client.SendEncryptedAsync(session, "/api/v1/plans/get", new { plan_id = planId }, session.ScopedToken);
        var status = await _client.SendEncryptedAsync(session, "/api/v1/plans/status", new { plan_id = planId }, session.ScopedToken);

        edge.StatusCode.Should().Be(HttpStatusCode.OK, "the add-edge response was {0}", edge);
        validate.StatusCode.Should().Be(HttpStatusCode.OK, "the validate response was {0}", validate);
        get.StatusCode.Should().Be(HttpStatusCode.OK, "the get response was {0}", get);
        status.StatusCode.Should().Be(HttpStatusCode.OK, "the status response was {0}", status);
        _fixture.FindPlan(planId!)!.TotalNodes.Should().Be(2);
    }

    // ── Task scope: context/write, context/list ────────────────────────────────────────

    [Fact]
    public async Task ContextWrite_IntoAnotherTask_IsForbidden()
    {
        var scope = OpenTaskScope();
        var newDocumentId = Id("doc_authz_new");
        var otherDocumentId = Id("doc_authz_other");
        _fixture.SeedContextEntry(
            otherDocumentId, Id("key_authz"), "original", "{\"read\":[\"*\"]}", scope.OtherPrincipalId, scope.OtherTaskId);

        // 1) A new document placed in the other task.
        var intoOtherTask = await WriteContextAsync(scope.Session, newDocumentId, scope.OtherTaskId);
        // 2) A new version of the other task's document, without a task and with the caller's own task.
        var overOtherDocument = await WriteContextAsync(scope.Session, otherDocumentId, taskId: null);
        var overOtherDocumentAsOwnTask = await WriteContextAsync(scope.Session, otherDocumentId, scope.OwnTaskId);

        intoOtherTask.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", intoOtherTask);
        overOtherDocument.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", overOtherDocument);
        overOtherDocumentAsOwnTask.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", overOtherDocumentAsOwnTask);
        _fixture.FindContextEntries(newDocumentId).Should().BeEmpty("a forbidden write must not store anything");
        _fixture.FindContextEntries(otherDocumentId).Should().ContainSingle("a forbidden write must not add a version")
            .Which.ContentRef.Should().Be("original");
    }

    [Fact]
    public async Task ContextWriteAndList_ForOwnTask_Succeed()
    {
        var scope = OpenTaskScope();
        var documentId = Id("doc_authz_own");

        var created = await WriteContextAsync(scope.Session, documentId, scope.OwnTaskId, contentRef: "v1");
        var updated = await WriteContextAsync(scope.Session, documentId, scope.OwnTaskId, contentRef: "v2");
        var list = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/list", new { task_id = scope.OwnTaskId }, scope.Session.ScopedToken);

        created.StatusCode.Should().Be(HttpStatusCode.OK, "the first write response was {0}", created);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, "the second write response was {0}", updated);
        _fixture.FindContextEntries(documentId).Select(entry => entry.ContentRef).Should().Equal("v1", "v2");
        list.StatusCode.Should().Be(HttpStatusCode.OK, "the list response was {0}", list);
        list.Body.Should().Contain(documentId, "the list response was {0}", list);
    }

    [Fact]
    public async Task ContextList_ForAnotherTask_IsForbidden()
    {
        var scope = OpenTaskScope();
        _fixture.SeedContextEntry(
            Id("doc_authz_other"), Id("key_authz"), "other task content", "{\"read\":[\"*\"]}", scope.OtherPrincipalId, scope.OtherTaskId);

        var reply = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/list", new { task_id = scope.OtherTaskId }, scope.Session.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", reply);
        reply.Body.Should().NotContain("other task content", "the response was {0}", reply);
    }

    // ── Task scope: execution-requests/query ───────────────────────────────────────────

    [Fact]
    public async Task ExecutionRequestQuery_IsLimitedToTheRequestsTask()
    {
        var scope = OpenTaskScope();
        var otherRequestId = _fixture.SeedExecutionRequest(scope.OtherTaskId, scope.OtherPrincipalId);
        var ownRequestId = _fixture.SeedExecutionRequest(scope.OwnTaskId, scope.Session.PrincipalId);

        var other = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/execution-requests/query", new { request_id = otherRequestId }, scope.Session.ScopedToken);
        var own = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/execution-requests/query", new { request_id = ownRequestId }, scope.Session.ScopedToken);

        other.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the other-task response was {0}", other);
        other.Body.Should().NotContain(otherRequestId, "the other-task response was {0}", other);
        own.StatusCode.Should().Be(HttpStatusCode.OK, "the own-task response was {0}", own);
        own.Body.Should().Contain(ownRequestId, "the own-task response was {0}", own);
    }

    // ── Shared context: read scope and ACL format ──────────────────────────────────────

    [Fact]
    public async Task ContextReadByKey_WithoutTaskId_IsScopedToTheCallersTask()
    {
        var key = Id("key_authz_shared");
        var victim = OpenTaskScope();
        var other = OpenTaskScope();
        var unrelated = OpenTaskScope();

        var victimWrite = await WriteContextAsync(
            victim.Session, Id("doc_authz_victim"), victim.OwnTaskId, key: key, contentRef: "victim content",
            acl: new { read = new[] { victim.Session.PrincipalId } });
        victimWrite.StatusCode.Should().Be(HttpStatusCode.OK, "the victim write response was {0}", victimWrite);

        // Another session writes the same key with higher version numbers, readable by everyone:
        // once without naming a task (the document lands in the writer's own task) and once naming its task.
        var unnamedTaskDocumentId = Id("doc_authz_unnamed_task");
        var otherTaskDocumentId = Id("doc_authz_other_task");
        for (var version = 0; version < 3; version++)
        {
            (await WriteContextAsync(other.Session, unnamedTaskDocumentId, taskId: null, key: key, contentRef: "other content"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await WriteContextAsync(other.Session, otherTaskDocumentId, other.OwnTaskId, key: key, contentRef: "other content"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _fixture.FindContextEntries(unnamedTaskDocumentId).Should().HaveCount(3)
            .And.OnlyContain(entry => entry.TaskId == other.OwnTaskId, "a write without task_id is scoped to the caller's task");

        var victimRead = await ReadByKeyAsync(victim.Session, key);
        var otherRead = await ReadByKeyAsync(other.Session, key);
        var unrelatedRead = await ReadByKeyAsync(unrelated.Session, key);

        victimRead.StatusCode.Should().Be(HttpStatusCode.OK, "the victim read response was {0}", victimRead);
        BrokerJson.ReadString(victimRead.Body, "data", "contentRef").Should().Be("victim content", "the victim read response was {0}", victimRead);
        BrokerJson.ReadString(otherRead.Body, "data", "taskId").Should().Be(other.OwnTaskId, "the other read response was {0}", otherRead);
        unrelatedRead.StatusCode.Should().Be(HttpStatusCode.NotFound, "a caller whose task has no such key gets nothing; the response was {0}", unrelatedRead);
    }

    [Theory]
    [InlineData("\"read-all\"")]
    [InlineData("[\"*\"]")]
    [InlineData("null")]
    [InlineData("{\"read\":\"*\"}")]
    [InlineData("{\"read\":[1]}")]
    [InlineData("{\"read\":[\"*\",null]}")]
    [InlineData("{\"read\":[{\"id\":\"*\"}]}")]
    public async Task ContextWrite_WithMalformedAcl_IsBadRequest(string aclJson)
    {
        var scope = OpenTaskScope();
        var documentId = Id("doc_authz_acl");
        var payload = new JsonObject
        {
            ["document_id"] = documentId,
            ["key"] = Id("key_authz"),
            ["content_ref"] = "content",
            ["task_id"] = scope.OwnTaskId,
            ["acl"] = JsonNode.Parse(aclJson)
        };

        var reply = await _client.SendEncryptedAsync(scope.Session, "/api/v1/context/write", payload, scope.Session.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the response was {0}", reply);
        _fixture.FindContextEntries(documentId).Should().BeEmpty("a rejected write must not store anything");
    }

    [Theory]
    [InlineData("\"read-all\"")]
    [InlineData("[\"*\"]")]
    [InlineData("{\"read\":\"*\"}")]
    [InlineData("{\"read\":[1,\"*\"]}")]
    [InlineData("{\"read\":[null,\"*\"]}")]
    public async Task ContextRead_WithMalformedStoredAcl_IsDeniedNotAnError(string storedAcl)
    {
        var scope = OpenTaskScope();
        var documentId = Id("doc_authz_stored_acl");
        var key = Id("key_authz_stored_acl");
        _fixture.SeedContextEntry(documentId, key, "restricted content", storedAcl, scope.Session.PrincipalId, scope.OwnTaskId);

        var read = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read", new { document_id = documentId }, scope.Session.ScopedToken);
        var byKey = await ReadByKeyAsync(scope.Session, key);
        var history = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/history", new { document_id = documentId }, scope.Session.ScopedToken);
        var list = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/list", new { task_id = scope.OwnTaskId }, scope.Session.ScopedToken);

        // A malformed ACL grants nothing (fail-closed) and never turns into a server error.
        read.StatusCode.Should().Be(HttpStatusCode.NotFound, "the read response was {0}", read);
        byKey.StatusCode.Should().Be(HttpStatusCode.NotFound, "the read-by-key response was {0}", byKey);
        history.StatusCode.Should().Be(HttpStatusCode.NotFound, "the history response was {0}", history);
        list.StatusCode.Should().Be(HttpStatusCode.OK, "the list response was {0}", list);
        foreach (var reply in new[] { read, byKey, history, list })
        {
            reply.Body.Should().NotContain("restricted content", "the response was {0}", reply);
        }
    }

    // ── Shared context: task scope of reads ────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextReads_OutsideTheCallersScope_RevealNothing(bool systemScope)
    {
        // A document readable by everyone per its ACL, either in the system scope (named like the
        // documents the broker maintains for its users) or in a task that belongs to another principal.
        var scope = OpenTaskScope();
        var taskId = systemScope ? SystemTaskId : scope.OtherTaskId;
        var author = systemScope ? "system:authz-regression" : scope.OtherPrincipalId;
        var documentId = systemScope ? Id("hlm.profile.line.authz") : Id("doc_authz_scoped");
        var key = Id("key_authz_scoped");
        _fixture.SeedContextEntry(documentId, key, "out-of-scope content v1", OpenAcl, author, taskId, version: 1);
        _fixture.SeedContextEntry(documentId, key, "out-of-scope content v2", OpenAcl, author, taskId, version: 2);

        var read = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read", new { document_id = documentId }, scope.Session.ScopedToken);
        var history = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/history", new { document_id = documentId }, scope.Session.ScopedToken);
        var byKey = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read-by-key", new { key, task_id = taskId }, scope.Session.ScopedToken);

        // Out of scope looks the same as a missing document for document reads; naming a task the
        // caller may not access is refused like context/list.
        read.StatusCode.Should().Be(HttpStatusCode.NotFound, "the read response was {0}", read);
        history.StatusCode.Should().Be(HttpStatusCode.NotFound, "the history response was {0}", history);
        byKey.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the read-by-key response was {0}", byKey);
        foreach (var reply in new[] { read, history, byKey })
        {
            reply.Body.Should().NotContain("out-of-scope content", "the response was {0}", reply);
        }
    }

    [Fact]
    public async Task ContextReads_OfSystemScopeDocuments_AreAvailableToAdmin()
    {
        var admin = _client.OpenSession(AdminRole);
        var documentId = Id("hlm.profile.line.authz");
        var key = Id("key_authz_system");
        _fixture.SeedContextEntry(documentId, key, "system content", OpenAcl, "system:authz-regression", SystemTaskId);

        var read = await _client.SendEncryptedAsync(admin, "/api/v1/context/read", new { document_id = documentId }, admin.ScopedToken);
        var history = await _client.SendEncryptedAsync(admin, "/api/v1/context/history", new { document_id = documentId }, admin.ScopedToken);
        var byKey = await _client.SendEncryptedAsync(
            admin, "/api/v1/context/read-by-key", new { key, task_id = SystemTaskId }, admin.ScopedToken);

        foreach (var reply in new[] { read, history, byKey })
        {
            reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
            reply.Body.Should().Contain("system content", "the response was {0}", reply);
        }
    }

    [Fact]
    public async Task ContextReads_OfOwnTasksDocument_Succeed()
    {
        var scope = OpenTaskScope();
        var documentId = Id("doc_authz_own_read");
        var key = Id("key_authz_own_read");
        (await WriteContextAsync(scope.Session, documentId, scope.OwnTaskId, key: key, contentRef: "own v1"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await WriteContextAsync(scope.Session, documentId, scope.OwnTaskId, key: key, contentRef: "own v2"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read", new { document_id = documentId }, scope.Session.ScopedToken);
        var history = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/history", new { document_id = documentId }, scope.Session.ScopedToken);
        var byNamedTask = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read-by-key", new { key, task_id = scope.OwnTaskId }, scope.Session.ScopedToken);
        var byOwnTask = await ReadByKeyAsync(scope.Session, key);

        read.StatusCode.Should().Be(HttpStatusCode.OK, "the read response was {0}", read);
        BrokerJson.ReadString(read.Body, "data", "contentRef").Should().Be("own v2", "the read response was {0}", read);
        history.StatusCode.Should().Be(HttpStatusCode.OK, "the history response was {0}", history);
        ReadDataContentRefs(history.Body).Should().Equal(new[] { "own v1", "own v2" }, "the history response was {0}", history);
        byNamedTask.StatusCode.Should().Be(HttpStatusCode.OK, "the read-by-key (named task) response was {0}", byNamedTask);
        byOwnTask.StatusCode.Should().Be(HttpStatusCode.OK, "the read-by-key (own task) response was {0}", byOwnTask);
    }

    [Fact]
    public async Task ContextReads_OfTasklessDocument_AreLimitedToItsAuthor()
    {
        // Documents without a task predate task-scoped writes; only their author (or an admin) may read them.
        var scope = OpenTaskScope();
        var ownDocumentId = Id("doc_authz_taskless_own");
        var otherDocumentId = Id("doc_authz_taskless_other");
        _fixture.SeedContextEntry(ownDocumentId, Id("key_authz"), "taskless own content", OpenAcl, scope.Session.PrincipalId, taskId: null);
        _fixture.SeedContextEntry(otherDocumentId, Id("key_authz"), "taskless other content", OpenAcl, scope.OtherPrincipalId, taskId: null);

        var ownRead = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read", new { document_id = ownDocumentId }, scope.Session.ScopedToken);
        var otherRead = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/read", new { document_id = otherDocumentId }, scope.Session.ScopedToken);
        var otherHistory = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/history", new { document_id = otherDocumentId }, scope.Session.ScopedToken);

        ownRead.StatusCode.Should().Be(HttpStatusCode.OK, "the own read response was {0}", ownRead);
        otherRead.StatusCode.Should().Be(HttpStatusCode.NotFound, "the other read response was {0}", otherRead);
        otherHistory.StatusCode.Should().Be(HttpStatusCode.NotFound, "the other history response was {0}", otherHistory);
        otherRead.Body.Should().NotContain("taskless other content");
        otherHistory.Body.Should().NotContain("taskless other content");
    }

    [Fact]
    public async Task ContextHistory_ReturnsOnlyVersionsTheCallerMaySee()
    {
        var scope = OpenTaskScope();
        var documentId = Id("doc_authz_history");
        var key = Id("key_authz_history");
        // v1: another task; v2: own task but its ACL names someone else; v3: own task, readable by everyone.
        _fixture.SeedContextEntry(documentId, key, "history v1 other task", OpenAcl, scope.OtherPrincipalId, scope.OtherTaskId, version: 1);
        _fixture.SeedContextEntry(
            documentId, key, "history v2 restricted", $"{{\"read\":[\"{scope.OtherPrincipalId}\"]}}", scope.Session.PrincipalId, scope.OwnTaskId, version: 2);
        _fixture.SeedContextEntry(documentId, key, "history v3 open", OpenAcl, scope.Session.PrincipalId, scope.OwnTaskId, version: 3);

        var history = await _client.SendEncryptedAsync(
            scope.Session, "/api/v1/context/history", new { document_id = documentId }, scope.Session.ScopedToken);

        history.StatusCode.Should().Be(HttpStatusCode.OK, "the history response was {0}", history);
        ReadDataContentRefs(history.Body).Should().Equal(new[] { "history v3 open" }, "the history response was {0}", history);
    }

    // ── Shared context: task scope of writes and system document ids ────────────────────

    [Fact]
    public async Task ContextWrite_WithoutTaskId_LandsInTheCallersTask()
    {
        var scope = OpenTaskScope();
        var documentId = Id("doc_authz_unnamed");

        var reply = await WriteContextAsync(scope.Session, documentId, taskId: null);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
        _fixture.FindContextEntries(documentId).Should().ContainSingle()
            .Which.TaskId.Should().Be(scope.OwnTaskId);
    }

    [Fact]
    public async Task ContextWrite_WithoutAnyTaskScope_IsForbidden()
    {
        // A token that is not bound to a task cannot create a document outside every task.
        var session = _client.OpenSession(ReaderRole, taskId: string.Empty);
        var documentId = Id("doc_authz_no_task");

        var reply = await WriteContextAsync(session, documentId, taskId: null);

        reply.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", reply);
        _fixture.FindContextEntries(documentId).Should().BeEmpty("a forbidden write must not store anything");
    }

    [Theory]
    [InlineData("hlm.profile.line.", true)]
    [InlineData("hlm.registration-policy.line", false)]
    [InlineData("hlm.memory.line.", true)]
    [InlineData("HLM.Profile.line.", true)]
    [InlineData("convlog:", true)]
    [InlineData("node_output_", true)]
    [InlineData("browser.execution.", true)]
    [InlineData("deployment.execution.", true)]
    public async Task ContextWrite_ToSystemDocumentId_IsForbiddenForNonAdmin(string documentIdPrefix, bool unique)
    {
        var scope = OpenTaskScope();
        var documentId = unique ? Id(documentIdPrefix + "authz") : documentIdPrefix;

        // Neither in the caller's own task nor without naming a task.
        var intoOwnTask = await WriteContextAsync(scope.Session, documentId, scope.OwnTaskId, contentRef: "{\"AccessTier\":\"member\"}");
        var withoutTask = await WriteContextAsync(scope.Session, documentId, taskId: null, contentRef: "{\"AccessTier\":\"member\"}");

        intoOwnTask.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", intoOwnTask);
        withoutTask.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the response was {0}", withoutTask);
        if (unique)
        {
            _fixture.FindContextEntries(documentId).Should().BeEmpty("a forbidden write must not store anything");
        }
        else
        {
            // A fixed system document id may already hold versions written by the broker or by other tests.
            _fixture.FindContextEntries(documentId)
                .Should().NotContain(entry => entry.AuthorPrincipalId == scope.Session.PrincipalId, "a forbidden write must not store anything");
        }
    }

    [Fact]
    public async Task ContextWrite_ToSystemDocumentId_IsAllowedForAdmin()
    {
        var admin = _client.OpenSession(AdminRole);
        var documentId = Id("hlm.authz-regression");

        var reply = await WriteContextAsync(admin, documentId, SystemTaskId);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "the response was {0}", reply);
        _fixture.FindContextEntries(documentId).Should().ContainSingle().Which.TaskId.Should().Be(SystemTaskId);
    }

    [Fact]
    public async Task PlantedSystemDocuments_DoNotChangeWhatTheBrokerGrants()
    {
        // Versions stored under system document ids by a principal other than a broker component are
        // ignored by the broker, even when they are the newest versions.
        var planter = _client.OpenSession(ReaderRole);
        var portalUserId = Id("authz.portal");
        var plantedProfile = new HighLevelUserProfile
        {
            Channel = "line",
            UserId = portalUserId,
            AccessTier = HighLevelAccessTier.Member,
            Permissions = new HighLevelUserPermissions
            {
                AllowQuery = true,
                AllowTransport = true,
                AllowProduction = true,
                AllowBrowserDelegated = true,
                AllowDeployment = true
            }
        };
        _fixture.SeedContextEntry(
            $"hlm.profile.line.{portalUserId}", $"hlm.profile.line.{portalUserId}",
            JsonSerializer.Serialize(plantedProfile), OpenAcl, planter.PrincipalId, SystemTaskId);
        _fixture.SeedContextEntry(
            "hlm.registration-policy.line", "hlm.registration-policy.line",
            JsonSerializer.Serialize(new HighLevelRegistrationPolicyState { Policy = HighLevelAnonymousRegistrationPolicy.DenyAll }),
            OpenAcl, planter.PrincipalId, SystemTaskId, version: 1_000_000);

        var register = await _client.SendPlainAsync(
            "POST",
            "/api/v1/portal/auth/register",
            JsonSerializer.Serialize(new { user_id = portalUserId, password = "authz-regression-password", display_name = "authz" }));
        var admin = _client.OpenSession(AdminRole);
        var policy = await _client.SendPlainAsync("GET", "/api/v1/high-level/line/registration-policy", bearerToken: admin.ScopedToken);

        register.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", register);
        BrokerJson.ReadString(register.Body, "data", "access_tier").Should().Be(HighLevelAccessTier.Basic, "the register response was {0}", register);
        policy.StatusCode.Should().Be(HttpStatusCode.OK, "the policy response was {0}", policy);
        BrokerJson.ReadString(policy.Body, "data", "policy").Should().NotBe(HighLevelAnonymousRegistrationPolicy.DenyAll, "the policy response was {0}", policy);
    }

    // ── Portal artifact listing ────────────────────────────────────────────────────────

    [Fact]
    public async Task PortalArtifactList_ReturnsOnlyTheSignedInUsersOwnArtifacts()
    {
        // Portal user ids may contain '_' and '.', and anyone can self-register one; the listing must
        // match the signed-in user id exactly: no wildcard, no case folding, no shared prefix.
        var suffix = Guid.NewGuid().ToString("N");
        var alice = $"alice{suffix}";
        var dotted = $"bob{suffix}.x";
        var lineFormat = $"U{suffix}"; // 'U' and 32 hex digits, the LINE user id format
        var owners = new[] { alice, dotted, lineFormat };
        var artifactIds = owners.ToDictionary(owner => owner, owner => _fixture.RecordLineArtifact(owner).ArtifactId);

        var others = new[]
        {
            $"alic_{suffix}",           // '_' in place of one character
            alice.ToUpperInvariant(),   // differs only in case
            $"bob{suffix}",             // another user's id up to its '.'
            "U" + new string('_', 32),  // the LINE user id shape, every character '_'
        };

        foreach (var other in others)
        {
            var listing = await GetPortalArtifactsAsync(await RegisterPortalUserAsync(other));

            listing.StatusCode.Should().Be(HttpStatusCode.OK, "the listing for {0} was {1}", other, listing);
            ReadPortalArtifactTotal(listing.Body).Should().Be(0, "the listing for {0} was {1}", other, listing);
            ReadPortalArtifactIds(listing.Body).Should().BeEmpty("the listing for {0} was {1}", other, listing);
            foreach (var artifactId in artifactIds.Values)
            {
                listing.Body.Should().NotContain(artifactId, "the listing for {0} must not reveal another user's artifact", other);
            }
        }

        // Each owner still sees its own artifact, and only that one.
        foreach (var owner in owners)
        {
            var listing = await GetPortalArtifactsAsync(await RegisterPortalUserAsync(owner));

            listing.StatusCode.Should().Be(HttpStatusCode.OK, "the listing for {0} was {1}", owner, listing);
            ReadPortalArtifactIds(listing.Body).Should().Equal(new[] { artifactIds[owner] }, "the listing for {0} was {1}", owner, listing);
        }
    }

    /// <summary>Self-registers a portal user and returns its session cookie (the fixture client keeps no cookies).</summary>
    private async Task<string> RegisterPortalUserAsync(string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/portal/auth/register")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { user_id = userId, password = PortalPassword }),
                Encoding.UTF8,
                "application/json")
        };
        using var response = await _fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "registering portal user {0} returned {1}", userId, body);
        response.Headers.TryGetValues("Set-Cookie", out var setCookies)
            .Should().BeTrue("registering portal user {0} must start a portal session", userId);
        return setCookies!
            .Select(header => header.Split(';', 2)[0])
            .Single(pair => pair.StartsWith(PortalAuthService.SessionCookieName + "=", StringComparison.Ordinal));
    }

    private async Task<BrokerReply> GetPortalArtifactsAsync(string sessionCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/portal/artifacts?limit=100");
        request.Headers.Add("Cookie", sessionCookie);
        using var response = await _fixture.Client.SendAsync(request);
        return new BrokerReply(response.StatusCode, await response.Content.ReadAsStringAsync(), decrypted: false);
    }

    private static int ReadPortalArtifactTotal(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("total").GetInt32();
    }

    private static IReadOnlyList<string?> ReadPortalArtifactIds(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("artifact_id").GetString())
            .ToList();
    }

    private static IReadOnlyList<string?> ReadDataContentRefs(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(entry => entry.TryGetProperty("contentRef", out var value) ? value.GetString() : null)
            .ToList();
    }

    private static string Id(string prefix) => EncryptedBrokerClient.NewId(prefix);

    /// <summary>
    /// A non-admin session bound to its own task, plus a second task that belongs to another principal.
    /// </summary>
    private TaskScope OpenTaskScope()
    {
        var owner = Id("prn_authz_owner");
        var otherPrincipal = Id("prn_authz_other");
        var ownTaskId = Id("task_authz_own");
        var otherTaskId = Id("task_authz_other");
        _fixture.SeedTask(ownTaskId, "query", submittedBy: "system", assignedPrincipalId: owner, assignedRoleId: ReaderRole);
        _fixture.SeedTask(otherTaskId, "query", submittedBy: otherPrincipal, assignedPrincipalId: otherPrincipal, assignedRoleId: ReaderRole);
        return new TaskScope(_client.OpenSession(ReaderRole, owner, ownTaskId), ownTaskId, otherTaskId, otherPrincipal);
    }

    private Task<BrokerReply> WriteContextAsync(
        BrokerTestSession session,
        string documentId,
        string? taskId,
        string? key = null,
        string contentRef = "content",
        object? acl = null)
    {
        var payload = new JsonObject
        {
            ["document_id"] = documentId,
            ["key"] = key ?? Id("key_authz"),
            ["content_ref"] = contentRef,
            ["acl"] = JsonSerializer.SerializeToNode(acl ?? new { read = new[] { "*" } })
        };
        if (taskId is not null)
        {
            payload["task_id"] = taskId;
        }

        return _client.SendEncryptedAsync(session, "/api/v1/context/write", payload, session.ScopedToken);
    }

    private Task<BrokerReply> ReadByKeyAsync(BrokerTestSession session, string key)
        => _client.SendEncryptedAsync(session, "/api/v1/context/read-by-key", new { key }, session.ScopedToken);

    private sealed record TaskScope(BrokerTestSession Session, string OwnTaskId, string OtherTaskId, string OtherPrincipalId);

    /// <summary>
    /// Keeps the token well-formed but replaces the tail of its signature segment. Both replacements
    /// are canonical base64url endings ('A' and 'Q' leave the low four bits of the last character zero).
    /// </summary>
    private static string AlterSignature(string token)
    {
        var replacement = token.EndsWith("AAAA", StringComparison.Ordinal) ? "AAAQ" : "AAAA";
        return token[..^4] + replacement;
    }

    private static void AssertRegistrationRejected(HandshakeResult result)
    {
        result.Status.Should().BeInRange(400, 499, "the register response was {0}", result);
        result.Session.Should().BeNull("no scoped token may be issued; the register response was {0}", result);
    }
}
