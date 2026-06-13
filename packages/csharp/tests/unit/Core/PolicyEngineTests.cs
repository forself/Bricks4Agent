using BrokerCore.Contracts;
using BrokerCore.Services;
using BrokerCore.Models;

namespace Unit.Tests.Core;

public class PolicyEngineTests
{
    private readonly PolicyEngine _sut;

    public PolicyEngineTests()
    {
        var schemaValidator = Substitute.For<ISchemaValidator>();
        schemaValidator.Validate(Arg.Any<string>(), Arg.Any<string>())
            .Returns((true, (string?)null));

        _sut = new PolicyEngine(schemaValidator, new PolicyEngineOptions());
    }

    private static ExecutionRequest MakeRequest(string? payload = null) => new()
    {
        RequestId = "req_test",
        TaskId = "task_test",
        SessionId = "ses_test",
        PrincipalId = "p_test",
        CapabilityId = "file.read",
        Intent = "read file",
        RequestPayload = payload ?? """{"route":"file.read","args":{}}""",
        IdempotencyKey = "idem_1",
        TraceId = "trace_1"
    };

    private static Capability MakeCapability(RiskLevel risk = RiskLevel.Low, string approvalPolicy = "auto") => new()
    {
        CapabilityId = "file.read",
        Route = "file.read",
        RiskLevel = risk,
        ParamSchema = "{}",
        ApprovalPolicy = approvalPolicy
    };

    private static CapabilityGrant MakeGrant(string scopeOverride = "{}") => new()
    {
        GrantId = "grt_test",
        TaskId = "task_test",
        SessionId = "ses_test",
        PrincipalId = "p_test",
        CapabilityId = "file.read",
        ScopeOverride = scopeOverride,
        RemainingQuota = -1,
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    private static BrokerTask MakeTask() => new()
    {
        TaskId = "task_test",
        TaskType = "file_operation",
        ScopeDescriptor = "{}"
    };

    // --- Rule 1: Epoch ---

    [Fact]
    public void Evaluate_MatchingEpoch_Passes()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Allow);
    }

    [Fact]
    public void Evaluate_StaleEpoch_Denied()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(), MakeGrant(), MakeTask(),
            currentEpoch: 5, tokenEpoch: 3);
        result.Decision.Should().Be(PolicyDecision.Deny);
        result.Reason.Should().Contain("epoch");
    }

    // --- Rule 2: Risk Level ---

    [Fact]
    public void Evaluate_LowRisk_Passes()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(RiskLevel.Low), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Allow);
    }

    // §18.2: High/Critical no longer hard-denied — they route to approval.
    [Fact]
    public void Evaluate_HighRisk_RequiresApproval()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(RiskLevel.High), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public void Evaluate_CriticalRisk_RequiresApproval()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(RiskLevel.Critical), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    // §18.2: approval_policy drives the decision for Low/Medium risk.
    [Fact]
    public void Evaluate_DenyPolicy_Denied()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(RiskLevel.Medium, "deny"), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Fact]
    public void Evaluate_RequireApprovalPolicy_RequiresApproval()
    {
        var result = _sut.Evaluate(MakeRequest(), MakeCapability(RiskLevel.Medium, "require_approval"), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public void Evaluate_AutoIfScopeMatch_InScope_Allows()
    {
        // grant scope restricts to "docs"; payload writes under docs -> in scope -> auto
        var payload = """{"route":"file.read","args":{"path":"docs/a.txt"}}""";
        var result = _sut.Evaluate(
            MakeRequest(payload),
            MakeCapability(RiskLevel.Medium, "auto_if_task_scope_match"),
            MakeGrant("""{"paths":["docs"]}"""), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Allow);
    }

    [Fact]
    public void Evaluate_AutoIfScopeMatch_OutOfScope_RequiresApproval()
    {
        // grant scope restricts to "docs"; payload writes under "src" -> out of scope -> approval
        var payload = """{"route":"file.read","args":{"path":"src/b.txt"}}""";
        var result = _sut.Evaluate(
            MakeRequest(payload),
            MakeCapability(RiskLevel.Medium, "auto_if_task_scope_match"),
            MakeGrant("""{"paths":["docs"]}"""), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public void Evaluate_AutoPolicy_OutOfScope_Denied()
    {
        // plain "auto" capability must still stay within scope; out-of-scope is a hard boundary
        var payload = """{"route":"file.read","args":{"path":"src/b.txt"}}""";
        var result = _sut.Evaluate(
            MakeRequest(payload),
            MakeCapability(RiskLevel.Low, "auto"),
            MakeGrant("""{"paths":["docs"]}"""), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Deny);
    }

    // --- Rule 3: Route Mismatch ---

    [Fact]
    public void Evaluate_RouteMismatch_Denied()
    {
        var payload = """{"route":"command.exec","args":{}}""";
        var result = _sut.Evaluate(MakeRequest(payload), MakeCapability(), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Deny);
        result.Reason.Should().Contain("route");
    }

    // --- Rule 6: Blacklisted Command ---

    [Fact]
    public void Evaluate_BlacklistedCommand_Denied()
    {
        var payload = """{"route":"file.read","args":{"command":"rm -rf /"}}""";
        var result = _sut.Evaluate(MakeRequest(payload), MakeCapability(), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Deny);
    }

    // --- All Rules Pass ---

    [Fact]
    public void Evaluate_AllRulesPass_Allows()
    {
        var payload = """{"route":"file.read","args":{"path":"readme.md"}}""";
        var result = _sut.Evaluate(MakeRequest(payload), MakeCapability(), MakeGrant(), MakeTask(),
            currentEpoch: 1, tokenEpoch: 1);
        result.Decision.Should().Be(PolicyDecision.Allow);
    }
}
