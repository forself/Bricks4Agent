using System.Net;
using System.Text.Json.Nodes;
using BrokerCore.Models;
using Integration.Tests.Fixtures;

namespace Integration.Tests.Api;

/// <summary>
/// HTTP-level tests for the session registration credential. The expected behaviour:
///   - registration needs the task's registration secret in the sealed handshake payload;
///   - a missing, wrong, expired or revoked secret, a secret of another task, an unknown or inactive principal,
///     an unknown task and a task reassigned to another principal are all answered with the same 401 body,
///     and no session is created;
///   - a credential can be used again until it expires or is revoked (container restarts, compose up again);
///   - administrators issue, list and revoke credentials for tasks created through tasks/create; the secret
///     appears only in the issue response and a listing carries no secret material;
///   - stopping an agent revokes its credentials and sessions, so neither its tokens nor its secret work.
/// The task, role and local-host rules that apply once the credential is valid are covered by
/// <see cref="BrokerAuthorizationRegressionTests"/>.
/// </summary>
public sealed class RegistrationCredentialTests : IClassFixture<BrokerAuthorizationFixture>
{
    private const string ReaderRole = "role_reader";
    private const string AdminRole = "role_admin";
    private const string LoopbackAddress = "127.0.0.1";
    private const string RejectedMessage = "Registration rejected.";
    private const string ToolSpecListPath = "/api/v1/tool-specs/list";

    private readonly BrokerAuthorizationFixture _fixture;
    private readonly EncryptedBrokerClient _client;

    public RegistrationCredentialTests(BrokerAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _client = new EncryptedBrokerClient(fixture.Client, fixture.Services);
    }

    public enum RejectedCase
    {
        MissingSecret,
        WrongSecret,
        AnotherTasksSecret,
        ExpiredCredential,
        RevokedCredential,
        NoCredentialForTask,
        UnknownPrincipal,
        UnknownTask,
        InactivePrincipal,
        TaskReassigned
    }

    [Theory]
    [InlineData(RejectedCase.MissingSecret)]
    [InlineData(RejectedCase.WrongSecret)]
    [InlineData(RejectedCase.AnotherTasksSecret)]
    [InlineData(RejectedCase.ExpiredCredential)]
    [InlineData(RejectedCase.RevokedCredential)]
    [InlineData(RejectedCase.NoCredentialForTask)]
    [InlineData(RejectedCase.UnknownPrincipal)]
    [InlineData(RejectedCase.UnknownTask)]
    [InlineData(RejectedCase.InactivePrincipal)]
    [InlineData(RejectedCase.TaskReassigned)]
    public async Task Register_WithoutAValidCredential_IsRejectedWithTheSameResponse(RejectedCase rejectedCase)
    {
        var (principalId, taskId, secret) = Arrange(rejectedCase);

        var result = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress, registrationSecret: secret);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the register response was {0}", result);
        result.Session.Should().BeNull("no scoped token may be issued; the register response was {0}", result);
        WithoutTraceId(result.Body).Should().Be(await ReferenceRejectionAsync(), "every rejection must look the same; the register response was {0}", result);
        CountSessions(principalId, taskId).Should().Be(0, "a rejected registration must not create a session");
        if (secret is not null)
        {
            result.Body.Should().NotContain(secret, "the response must not echo the secret");
        }
    }

    [Fact]
    public async Task Register_WithTheSameCredentialTwice_SucceedsBothTimes()
    {
        var (principalId, taskId) = SeedReaderTask();
        var issued = _fixture.IssueRegistrationCredential(principalId, taskId);

        // A restarted container (or compose up again) registers with the same secret.
        var first = await _client.RegisterAsync(principalId, taskId, registrationSecret: issued.Secret);
        var second = await _client.RegisterAsync(principalId, taskId, registrationSecret: issued.Secret);

        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first register response was {0}", first);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "the second register response was {0}", second);
        first.Session!.SessionId.Should().NotBe(second.Session!.SessionId);

        var firstReply = await _client.SendEncryptedAsync(first.Session, "/api/v1/grants/list", payload: null, first.Session.ScopedToken);
        var secondReply = await _client.SendEncryptedAsync(second.Session, "/api/v1/grants/list", payload: null, second.Session.ScopedToken);
        firstReply.StatusCode.Should().Be(HttpStatusCode.OK, "the first session's response was {0}", firstReply);
        secondReply.StatusCode.Should().Be(HttpStatusCode.OK, "the second session's response was {0}", secondReply);

        var stored = _fixture.FindRegistrationCredential(issued.CredentialId)!;
        stored.UseCount.Should().Be(2);
        stored.LastUsedAt.Should().NotBeNull();
        stored.SecretHash.Should().NotBe(issued.Secret, "only the hash is stored");
        stored.SecretHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task AdminIssuedCredential_RegistersUntilRevoked_AndIsNeverListedWithItsSecret()
    {
        var admin = _client.OpenSession(AdminRole);
        var (principalId, taskId) = SeedReaderTask();

        var issue = await _client.SendEncryptedAsync(
            admin,
            "/api/v1/admin/registration-credentials/issue",
            new { principal_id = principalId, task_id = taskId, lifetime_hours = 2 },
            admin.ScopedToken);
        issue.StatusCode.Should().Be(HttpStatusCode.OK, "the issue response was {0}", issue);
        issue.Decrypted.Should().BeTrue("the secret travels only inside the encrypted response");
        var secret = BrokerJson.ReadString(issue.Body, "data", "registration_secret");
        var credentialId = BrokerJson.ReadString(issue.Body, "data", "credential_id");
        secret.Should().NotBeNullOrEmpty();
        credentialId.Should().NotBeNullOrEmpty();

        var registered = await _client.RegisterAsync(principalId, taskId, registrationSecret: secret);
        registered.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", registered);

        var list = await _client.SendEncryptedAsync(
            admin,
            "/api/v1/admin/registration-credentials/list",
            new { task_id = taskId },
            admin.ScopedToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK, "the list response was {0}", list);
        list.Body.Should().Contain(credentialId!);
        list.Body.Should().NotContain(secret!, "a listing must not contain the secret");
        list.Body.Should().NotContain(_fixture.FindRegistrationCredential(credentialId!)!.SecretHash, "a listing must not contain the hash");
        list.Body.Should().NotContain("secret_hash");

        var revoke = await _client.SendEncryptedAsync(
            admin,
            "/api/v1/admin/registration-credentials/revoke",
            new { credential_id = credentialId },
            admin.ScopedToken);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, "the revoke response was {0}", revoke);

        var afterRevoke = await _client.RegisterAsync(principalId, taskId, registrationSecret: secret);
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the register response was {0}", afterRevoke);
        WithoutTraceId(afterRevoke.Body).Should().Be(await ReferenceRejectionAsync());
    }

    [Theory]
    [InlineData("{\"principal_id\":\"__PRINCIPAL__\",\"task_id\":\"__OTHER_TASK__\"}")]
    [InlineData("{\"principal_id\":\"__PRINCIPAL__\",\"task_id\":\"__TASK__\",\"lifetime_hours\":1000}")]
    [InlineData("{\"principal_id\":\"__PRINCIPAL__\",\"task_id\":\"__TASK__\",\"lifetime_hours\":0}")]
    [InlineData("{\"principal_id\":\"__PRINCIPAL__\"}")]
    public async Task AdminIssue_ForATaskNotAssignedToThePrincipalOrWithABadLifetime_IsRefused(string template)
    {
        var admin = _client.OpenSession(AdminRole);
        var (principalId, taskId) = SeedReaderTask();
        var (_, otherTaskId) = SeedReaderTask();
        var payload = template
            .Replace("__PRINCIPAL__", principalId, StringComparison.Ordinal)
            .Replace("__OTHER_TASK__", otherTaskId, StringComparison.Ordinal)
            .Replace("__TASK__", taskId, StringComparison.Ordinal);

        var reply = await _client.SendEncryptedAsync(admin, "/api/v1/admin/registration-credentials/issue", payload, admin.ScopedToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the response was {0}", reply);
        reply.Body.Should().NotContain("registration_secret");
    }

    [Fact]
    public async Task AgentStop_RevokesTheAgentsCredentialsAndSessions()
    {
        var admin = _client.OpenSession(AdminRole);
        var agentName = $"rc{Guid.NewGuid():N}"[..14];
        var created = await _client.SendEncryptedAsync(
            admin,
            "/api/v1/agents/create",
            new { agent_id = agentName, display_name = "registration credential test", task_type = "analysis" },
            admin.ScopedToken);
        created.StatusCode.Should().Be(HttpStatusCode.OK, "the create response was {0}", created);
        var agentId = BrokerJson.ReadString(created.Body, "data", "agent_id")!;
        var principalId = BrokerJson.ReadString(created.Body, "data", "principal_id")!;
        var taskId = BrokerJson.ReadString(created.Body, "data", "task_id")!;

        // This host has no container runtime: spawn is refused before any credential is issued.
        var spawn = await _client.SendEncryptedAsync(admin, "/api/v1/agents/spawn", new { agent_id = agentId }, admin.ScopedToken);
        spawn.Status.Should().Be(503, "the spawn response was {0}", spawn);
        spawn.Body.Should().NotContain("registration_secret");
        _fixture.Db.Query<RegistrationCredential>(
                "SELECT * FROM registration_credentials WHERE task_id = @taskId",
                new { taskId })
            .Should().BeEmpty("a refused spawn must not leave a credential behind");

        var issued = _fixture.IssueRegistrationCredential(principalId, taskId);
        var registered = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress, registrationSecret: issued.Secret);
        registered.StatusCode.Should().Be(HttpStatusCode.OK, "the register response was {0}", registered);
        var agentSession = registered.Session!;
        var before = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: agentSession.ScopedToken);
        before.StatusCode.Should().Be(HttpStatusCode.OK, "the agent token works before stop; the response was {0}", before);

        var stop = await _client.SendEncryptedAsync(admin, "/api/v1/agents/stop", new { agent_id = agentId }, admin.ScopedToken);
        stop.StatusCode.Should().Be(HttpStatusCode.OK, "the stop response was {0}", stop);

        var bearerAfter = await _client.SendPlainAsync("POST", ToolSpecListPath, "{}", bearerToken: agentSession.ScopedToken);
        var channelAfter = await _client.SendEncryptedAsync(agentSession, "/api/v1/grants/list", payload: null, agentSession.ScopedToken);
        var registerAfter = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress, registrationSecret: issued.Secret);

        bearerAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the stopped agent's token must be rejected; the response was {0}", bearerAfter);
        channelAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the stopped agent's channel must be rejected; the response was {0}", channelAfter);
        registerAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the stopped agent cannot register again; the response was {0}", registerAfter);
        WithoutTraceId(registerAfter.Body).Should().Be(await ReferenceRejectionAsync());
        _fixture.FindRegistrationCredential(issued.CredentialId)!.RevokedAt.Should().NotBeNull();
        _fixture.FindSession(agentSession.SessionId)!.Status.Should().Be(SessionStatus.Revoked);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private (string PrincipalId, string TaskId, string? Secret) Arrange(RejectedCase rejectedCase)
    {
        var (principalId, taskId) = SeedReaderTask();
        switch (rejectedCase)
        {
            case RejectedCase.MissingSecret:
                _fixture.SeedRegistrationCredential(principalId, taskId);
                return (principalId, taskId, null);
            case RejectedCase.WrongSecret:
                _fixture.SeedRegistrationCredential(principalId, taskId);
                return (principalId, taskId, BrokerCore.Services.RegistrationCredentialService.GenerateSecret());
            case RejectedCase.AnotherTasksSecret:
            {
                _fixture.SeedRegistrationCredential(principalId, taskId);
                var (otherPrincipalId, otherTaskId) = SeedReaderTask();
                return (principalId, taskId, _fixture.SeedRegistrationCredential(otherPrincipalId, otherTaskId));
            }
            case RejectedCase.ExpiredCredential:
                return (principalId, taskId, _fixture.SeedRegistrationCredential(principalId, taskId, expiresAt: DateTime.UtcNow.AddMinutes(-1)));
            case RejectedCase.RevokedCredential:
                return (principalId, taskId, _fixture.SeedRegistrationCredential(principalId, taskId, revoked: true));
            case RejectedCase.NoCredentialForTask:
                return (principalId, taskId, BrokerCore.Services.RegistrationCredentialService.GenerateSecret());
            case RejectedCase.UnknownPrincipal:
            {
                // A credential exists for the pair, but the principal record does not.
                var ghost = EncryptedBrokerClient.NewId("prn_authz_ghost");
                var ghostTask = EncryptedBrokerClient.NewId("task_authz_ghost");
                _fixture.SeedTask(ghostTask, "query", submittedBy: "system", assignedPrincipalId: ghost, assignedRoleId: ReaderRole);
                return (ghost, ghostTask, _fixture.SeedRegistrationCredential(ghost, ghostTask));
            }
            case RejectedCase.UnknownTask:
            {
                var missingTask = EncryptedBrokerClient.NewId("task_authz_missing");
                return (principalId, missingTask, _fixture.SeedRegistrationCredential(principalId, missingTask));
            }
            case RejectedCase.InactivePrincipal:
            {
                var secret = _fixture.SeedRegistrationCredential(principalId, taskId);
                _fixture.Db.Execute(
                    "UPDATE principals SET status = @disabled WHERE principal_id = @principalId",
                    new { disabled = (int)EntityStatus.Disabled, principalId });
                return (principalId, taskId, secret);
            }
            case RejectedCase.TaskReassigned:
            {
                var secret = _fixture.SeedRegistrationCredential(principalId, taskId);
                var newOwner = EncryptedBrokerClient.NewId("prn_authz_new_owner");
                _fixture.SeedPrincipal(newOwner);
                _fixture.Db.Execute(
                    "UPDATE broker_tasks SET assigned_principal_id = @newOwner WHERE task_id = @taskId",
                    new { newOwner, taskId });
                return (principalId, taskId, secret);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(rejectedCase), rejectedCase, null);
        }
    }

    /// <summary>
    /// The body (without its per-response trace id) of a rejection whose cause is plainly a missing credential,
    /// to compare the other cases with.
    /// </summary>
    private async Task<string> ReferenceRejectionAsync()
    {
        var (principalId, taskId) = SeedReaderTask();
        var reference = await _client.RegisterAsync(principalId, taskId, remoteAddress: LoopbackAddress);
        reference.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the reference response was {0}", reference);
        reference.Body.Should().Contain(RejectedMessage);
        return WithoutTraceId(reference.Body);
    }

    /// <summary>The response body with the trace id (different on every response) removed.</summary>
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

    private (string PrincipalId, string TaskId) SeedReaderTask()
    {
        var principalId = EncryptedBrokerClient.NewId("prn_authz_rc");
        var taskId = EncryptedBrokerClient.NewId("task_authz_rc");
        _fixture.SeedPrincipal(principalId);
        _fixture.SeedTask(taskId, "query", submittedBy: "system", assignedPrincipalId: principalId, assignedRoleId: ReaderRole);
        return (principalId, taskId);
    }

    private int CountSessions(string principalId, string taskId)
        => _fixture.Db.Query<ContainerSession>(
                "SELECT * FROM container_sessions WHERE principal_id = @principalId AND task_id = @taskId",
                new { principalId, taskId })
            .Count;
}
