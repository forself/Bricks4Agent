using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Unit.Tests.Helpers;

namespace Unit.Tests.Core;

/// <summary>
/// 沒有可用授予時的拒絕理由。配額用完的授予被設為 Exhausted，GetActiveGrant 查不到它，
/// 先前一律回 "No active grant"；代理依「配額用完就停止」的流程判斷時需要正確的理由。
/// 拒絕決定不變，只有理由依最新一筆授予的狀態說明。
/// </summary>
public sealed class BrokerServiceGrantReasonTests : IDisposable
{
    private const string CapabilityId = "test.grant.reason";
    private const string Route = "validate_definition";
    private const string Payload = "{\"route\":\"validate_definition\",\"args\":{\"template\":{}},\"project_root\":\"\"}";

    private readonly BrokerDb _db = TestDb.CreateInMemory();

    public void Dispose() => _db.Dispose();

    private sealed class OkDispatcher : IExecutionDispatcher
    {
        public int Calls;

        public Task<ExecutionResult> DispatchAsync(ApprovedRequest request)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(ExecutionResult.Ok(request.RequestId, "{\"ok\":true}"));
        }
    }

    private (BrokerService Broker, OkDispatcher Dispatcher, CapabilityCatalog Catalog, string TaskId, string SessionId) Setup()
    {
        _db.Insert(new Capability
        {
            CapabilityId = CapabilityId,
            Route = Route,
            ResourceType = "generation",
            ParamSchema = "{}",
            RiskLevelValue = (int)RiskLevel.Low,
            ApprovalPolicy = "auto",
        });

        var catalog = new CapabilityCatalog(_db);
        var revocation = new RevocationService(_db);
        var sessions = new SessionService(_db);
        var dispatcher = new OkDispatcher();
        var broker = new BrokerService(
            _db,
            new PolicyEngine(new SchemaValidator(), new PolicyEngineOptions()),
            new AuditService(_db),
            catalog,
            sessions,
            revocation,
            new TaskRouter(),
            dispatcher);

        var task = broker.CreateTask("prn_owner", "analysis", "{}", "prn_agent", "role_executor");
        var session = sessions.RegisterSession(task.TaskId, "prn_agent", "role_executor", "jti_reason", revocation.GetCurrentEpoch(), string.Empty);
        return (broker, dispatcher, catalog, task.TaskId, session.SessionId);
    }

    private static Task<ExecutionRequest> Submit(BrokerService broker, string taskId, string sessionId, string key)
        => broker.SubmitExecutionRequestAsync("prn_agent", taskId, sessionId, CapabilityId, "Validate generation definition", Payload, key, $"trace_{key}");

    [Fact]
    public async Task AGrantWhoseQuotaIsUsedUp_IsDeniedWithQuotaExhausted()
    {
        var (broker, dispatcher, catalog, taskId, sessionId) = Setup();
        var grant = catalog.CreateGrant(taskId, sessionId, "prn_agent", CapabilityId, "{\"routes\":[\"validate_definition\"]}", 1, DateTime.UtcNow.AddHours(1));

        var first = await Submit(broker, taskId, sessionId, "first");
        first.ExecutionState.Should().Be(ExecutionState.Succeeded, first.PolicyReason);
        _db.Get<CapabilityGrant>(grant.GrantId)!.Status.Should().Be(GrantStatus.Exhausted, "the last call of the quota exhausts the grant");

        var second = await Submit(broker, taskId, sessionId, "second");
        second.ExecutionState.Should().Be(ExecutionState.Denied);
        second.PolicyDecision.Should().Be(PolicyDecision.Deny);
        second.PolicyReason.Should().Be("Grant quota exhausted.");
        dispatcher.Calls.Should().Be(1, "the denied request is not dispatched");
    }

    [Fact]
    public async Task ARevokedOrExpiredGrant_IsDeniedWithItsReason()
    {
        var (broker, dispatcher, catalog, taskId, sessionId) = Setup();
        var grant = catalog.CreateGrant(taskId, sessionId, "prn_agent", CapabilityId, "{}", 5, DateTime.UtcNow.AddHours(1));

        catalog.RevokeSessionGrants(sessionId);
        var revoked = await Submit(broker, taskId, sessionId, "revoked");
        revoked.ExecutionState.Should().Be(ExecutionState.Denied);
        revoked.PolicyReason.Should().Be("Grant revoked.");

        _db.Execute("UPDATE capability_grants SET status = 0, expires_at = @past WHERE grant_id = @id",
            new { past = DateTime.UtcNow.AddMinutes(-1), id = grant.GrantId });
        var expired = await Submit(broker, taskId, sessionId, "expired");
        expired.ExecutionState.Should().Be(ExecutionState.Denied);
        expired.PolicyReason.Should().Be("Grant expired.");

        _db.Execute("UPDATE capability_grants SET status = @expiredStatus WHERE grant_id = @id",
            new { expiredStatus = (int)GrantStatus.Expired, id = grant.GrantId });
        (await Submit(broker, taskId, sessionId, "expired-status")).PolicyReason.Should().Be("Grant expired.");
        dispatcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task WithoutAnyGrant_TheReasonStaysNoActiveGrant()
    {
        var (broker, dispatcher, catalog, taskId, sessionId) = Setup();
        // 同一任務、另一個 session 的授予不算這個 session 的授予。
        catalog.CreateGrant(taskId, "ses_other", "prn_agent", CapabilityId, "{}", 0, DateTime.UtcNow.AddHours(1));

        var denied = await Submit(broker, taskId, sessionId, "none");
        denied.ExecutionState.Should().Be(ExecutionState.Denied);
        denied.PolicyReason.Should().Be($"No active grant for capability '{CapabilityId}' in this task/session.");
        dispatcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task TheLatestGrantDecidesTheReason()
    {
        var (broker, _, catalog, taskId, sessionId) = Setup();
        var older = catalog.CreateGrant(taskId, sessionId, "prn_agent", CapabilityId, "{}", 1, DateTime.UtcNow.AddHours(1));
        _db.Execute("UPDATE capability_grants SET status = @revoked, issued_at = @earlier WHERE grant_id = @id",
            new { revoked = (int)GrantStatus.Revoked, earlier = DateTime.UtcNow.AddMinutes(-10), id = older.GrantId });
        var newer = catalog.CreateGrant(taskId, sessionId, "prn_agent", CapabilityId, "{}", 1, DateTime.UtcNow.AddHours(1));
        _db.Execute("UPDATE capability_grants SET status = @exhausted, remaining_quota = 0 WHERE grant_id = @id",
            new { exhausted = (int)GrantStatus.Exhausted, id = newer.GrantId });

        (await Submit(broker, taskId, sessionId, "latest")).PolicyReason.Should().Be("Grant quota exhausted.");
    }
}
