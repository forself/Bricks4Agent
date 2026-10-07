using BrokerCore.Contracts;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Unit.Tests.Core;

/// <summary>
/// PolicyEngine 的 scope 判斷是 fail-closed：scope 的 routes／paths 不是字串陣列，或判斷過程丟出例外，
/// 都視為不在 scope 內（auto → Deny；auto_if_task_scope_match → 送審），絕不因無法判斷而放行。
/// 也涵蓋生成能力的實際形狀：scope 只有 routes 與輸出設定、args 沒有路徑鍵時，route 相符才在 scope 內。
/// </summary>
public class PolicyEngineScopeFailClosedTests
{
    private readonly PolicyEngine _sut = new(new SchemaValidator(), new PolicyEngineOptions());

    private static ExecutionRequest Request(string payload) => new()
    {
        RequestId = "req_scope",
        TaskId = "task_scope",
        SessionId = "ses_scope",
        PrincipalId = "prn_scope",
        CapabilityId = "test.capability",
        Intent = "scope test",
        RequestPayload = payload,
        IdempotencyKey = "idem_scope",
        TraceId = "trace_scope",
    };

    private static Capability Capability(string route, string approvalPolicy, RiskLevel risk = RiskLevel.Low, string paramSchema = "{}") => new()
    {
        CapabilityId = "test.capability",
        Route = route,
        RiskLevel = risk,
        ParamSchema = paramSchema,
        ApprovalPolicy = approvalPolicy,
    };

    private static CapabilityGrant Grant(string scope) => new()
    {
        GrantId = "grt_scope",
        TaskId = "task_scope",
        SessionId = "ses_scope",
        PrincipalId = "prn_scope",
        CapabilityId = "test.capability",
        ScopeOverride = scope,
        RemainingQuota = -1,
        ExpiresAt = DateTime.UtcNow.AddHours(1),
    };

    private static BrokerTask Task(string scope = "{}") => new()
    {
        TaskId = "task_scope",
        TaskType = "system_scaffold",
        ScopeDescriptor = scope,
    };

    public static TheoryData<string> MalformedScopes => new()
    {
        """{"routes":"read_file"}""",
        """{"routes":["read_file",1]}""",
        """{"routes":[["read_file"]]}""",
        """{"routes":{"read_file":true}}""",
        """{"paths":"/workspace"}""",
        """{"paths":["/workspace",null]}""",
        """{"routes":["read_file"],"paths":[{"path":"/workspace"}]}""",
    };

    [Theory]
    [MemberData(nameof(MalformedScopes))]
    public void MalformedRoutesOrPaths_AreOutOfScope(string scope)
    {
        const string payload = """{"route":"read_file","args":{"path":"/workspace/a.txt"}}""";

        PolicyEngine.IsScopeValid(payload, "read_file", scope, "{}").Should().BeFalse();
        PolicyEngine.IsScopeValid(payload, "read_file", "{}", scope).Should().BeFalse("a malformed task scope is not a missing one");
    }

    [Theory]
    [MemberData(nameof(MalformedScopes))]
    public void MalformedScope_WithAutoPolicy_IsDenied(string scope)
    {
        var result = _sut.Evaluate(
            Request("""{"route":"read_file","args":{"path":"/workspace/a.txt"}}"""),
            Capability("read_file", "auto"),
            Grant(scope),
            Task(),
            currentEpoch: 1,
            tokenEpoch: 1);

        result.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Theory]
    [MemberData(nameof(MalformedScopes))]
    public void MalformedScope_WithAutoIfTaskScopeMatch_RequiresApproval(string scope)
    {
        var result = _sut.Evaluate(
            Request("""{"route":"read_file","args":{"path":"/workspace/a.txt"}}"""),
            Capability("read_file", "auto_if_task_scope_match", RiskLevel.Medium),
            Grant(scope),
            Task(),
            currentEpoch: 1,
            tokenEpoch: 1);

        result.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"routes":null}""")]
    [InlineData("""{"routes":[]}""")]
    public void AbsentOrEmptyRestrictions_StayUnrestricted(string scope)
    {
        PolicyEngine.IsScopeValid("""{"route":"read_file","args":{}}""", "read_file", scope, "{}").Should().BeTrue();
    }

    private const string GenerateScope =
        """{"routes":["generate_scaffold"],"output_slot":"task_1","package_name":"contacts","max_pages":12,"package":"definition-site-v1"}""";

    private const string GenerateSchema =
        """{"type":"object","required":["template"],"properties":{"template":{"type":"object"},"page_ids":{"type":"array","items":{"type":"string","maxLength":64}},"title":{"type":"string","maxLength":120}}}""";

    [Fact]
    public void GenerateScaffold_RouteInScopeWithoutPathKeys_IsAllowed()
    {
        var result = _sut.Evaluate(
            Request("""{"route":"generate_scaffold","args":{"template":{"kind":"definition-template"},"title":"Contacts"},"project_root":"/workspace"}"""),
            Capability("generate_scaffold", "auto_if_task_scope_match", RiskLevel.Medium, GenerateSchema),
            Grant(GenerateScope),
            Task(),
            currentEpoch: 1,
            tokenEpoch: 1);

        result.Decision.Should().Be(PolicyDecision.Allow);
    }

    [Fact]
    public void GenerateScaffold_RouteOutsideTheGrantRoutes_NeedsAdminApproval()
    {
        var result = _sut.Evaluate(
            Request("""{"route":"generate_scaffold","args":{"template":{}}}"""),
            Capability("generate_scaffold", "auto_if_task_scope_match", RiskLevel.Medium, GenerateSchema),
            Grant("""{"routes":["validate_definition"]}"""),
            Task(),
            currentEpoch: 1,
            tokenEpoch: 1);

        result.Decision.Should().Be(PolicyDecision.RequireApproval);
        result.RequiredApproverTier.Should().Be(ApproverTier.Admin);
    }

    [Fact]
    public void GenerateScaffold_PathTraversalInArgs_IsDeniedEvenThoughTheWorkerIgnoresIt()
    {
        var result = _sut.Evaluate(
            Request("""{"route":"generate_scaffold","args":{"template":{},"path":"../../outside"}}"""),
            Capability("generate_scaffold", "auto_if_task_scope_match", RiskLevel.Medium, GenerateSchema),
            Grant(GenerateScope),
            Task(),
            currentEpoch: 1,
            tokenEpoch: 1);

        result.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Fact]
    public void GenerateScaffold_SchemaViolation_IsDenied()
    {
        var result = _sut.Evaluate(
            Request("""{"route":"generate_scaffold","args":{"template":"not an object"}}"""),
            Capability("generate_scaffold", "auto_if_task_scope_match", RiskLevel.Medium, GenerateSchema),
            Grant(GenerateScope),
            Task(),
            currentEpoch: 1,
            tokenEpoch: 1);

        result.Decision.Should().Be(PolicyDecision.Deny);
    }
}
