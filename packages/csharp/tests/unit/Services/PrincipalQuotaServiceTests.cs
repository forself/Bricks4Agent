using Broker.Services;
using BrokerCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Services;

/// <summary>
/// H1 — PrincipalQuotaService 行為測試。
///
/// 重點不變式：
/// - 同 principal 累計 LLM tokens 跨多次 RecordLlmUsage call
/// - DefaultDailyLlmTokens 從 IConfiguration 讀
/// - soft mode（預設）：超 quota 還是回 Allowed=true
/// - enforce mode：超 quota 後回 Allowed=false
/// - Snapshot() 反映當下用量
/// - 不同 principal 用量隔離
/// - QUOTA_OVERRIDE_<pid> env 覆蓋預設值
///
/// daily reset 跨日測試需要操控時間、複雜度高、本層只跑一日內語意。
/// </summary>
public class PrincipalQuotaServiceTests
{
    private static IConfiguration Cfg(long defaultLlm = 1_000, long defaultDispatch = 100, bool enforce = false)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Quota:Default:LlmTokensPerDay"] = defaultLlm.ToString(),
            ["Quota:Default:DispatchesPerDay"] = defaultDispatch.ToString(),
            ["Quota:Enforce"] = enforce.ToString(),
        }).Build();

    private static PrincipalQuotaService NewService(IConfiguration cfg) =>
        new(cfg, audit: NSubstitute.Substitute.For<IAuditService>(), logger: new NullLogger<PrincipalQuotaService>());

    [Fact]
    public void DefaultsLoadFromConfig()
    {
        var svc = NewService(Cfg(defaultLlm: 5000, defaultDispatch: 50));
        svc.DefaultDailyLlmTokens.Should().Be(5000);
        svc.DefaultDailyDispatches.Should().Be(50);
        svc.EnforceMode.Should().BeFalse();
    }

    [Fact]
    public void RecordLlmUsage_AccumulatesPerPrincipal()
    {
        var svc = NewService(Cfg(defaultLlm: 1000));
        var (allow1, cur1, lim) = svc.RecordLlmUsage("prn_a", 200);
        allow1.Should().BeTrue();
        cur1.Should().Be(200);
        lim.Should().Be(1000);

        var (_, cur2, _) = svc.RecordLlmUsage("prn_a", 300);
        cur2.Should().Be(500);
    }

    [Fact]
    public void RecordLlmUsage_PrincipalsIsolated()
    {
        var svc = NewService(Cfg(defaultLlm: 1000));
        svc.RecordLlmUsage("prn_a", 400);
        svc.RecordLlmUsage("prn_b", 100);
        var snap = svc.Snapshot();
        snap["prn_a"].LlmTokensUsed.Should().Be(400);
        snap["prn_b"].LlmTokensUsed.Should().Be(100);
    }

    [Fact]
    public void SoftMode_OverLimitStillAllowed()
    {
        var svc = NewService(Cfg(defaultLlm: 100, enforce: false));
        var (allow, cur, lim) = svc.RecordLlmUsage("prn_x", 200);
        allow.Should().BeTrue("soft mode never blocks");
        cur.Should().Be(200);
        lim.Should().Be(100);
        svc.Snapshot()["prn_x"].LlmOverLimit.Should().BeTrue();
    }

    [Fact]
    public void EnforceMode_OverLimitBlocked()
    {
        var svc = NewService(Cfg(defaultLlm: 100, enforce: true));
        svc.RecordLlmUsage("prn_y", 80).Allowed.Should().BeTrue();    // 80 OK
        svc.RecordLlmUsage("prn_y", 30).Allowed.Should().BeFalse();   // 110 > 100 blocked
    }

    [Fact]
    public void RecordDispatch_Increments_AndBlocksAfterLimit_InEnforce()
    {
        var svc = NewService(Cfg(defaultDispatch: 3, enforce: true));
        svc.RecordDispatch("prn_z").Allowed.Should().BeTrue();   // 1
        svc.RecordDispatch("prn_z").Allowed.Should().BeTrue();   // 2
        svc.RecordDispatch("prn_z").Allowed.Should().BeTrue();   // 3
        svc.RecordDispatch("prn_z").Allowed.Should().BeFalse();  // 4 over
    }

    [Fact]
    public void EmptyPrincipalId_RoutesToUnknownBucket()
    {
        var svc = NewService(Cfg(defaultLlm: 500));
        svc.RecordLlmUsage("", 100);
        svc.RecordLlmUsage(null!, 50);
        var snap = svc.Snapshot();
        snap.ContainsKey("(unknown)").Should().BeTrue();
        snap["(unknown)"].LlmTokensUsed.Should().Be(150);
    }

    [Fact]
    public void NegativeTokens_DoNotDecrement()
    {
        var svc = NewService(Cfg(defaultLlm: 500));
        svc.RecordLlmUsage("prn_q", 100);
        svc.RecordLlmUsage("prn_q", -50);  // Math.Max(0, ...) 防 underflow
        svc.Snapshot()["prn_q"].LlmTokensUsed.Should().Be(100);
    }

    [Fact]
    public void Snapshot_OnlyIncludesPrincipalsWithUsage()
    {
        var svc = NewService(Cfg());
        svc.Snapshot().Should().BeEmpty();
        svc.RecordLlmUsage("prn_only", 1);
        svc.Snapshot().Should().ContainSingle().Which.Key.Should().Be("prn_only");
    }
}
