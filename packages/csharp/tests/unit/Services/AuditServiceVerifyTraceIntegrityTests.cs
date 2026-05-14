using BrokerCore.Data;
using BrokerCore.Services;
using Unit.Tests.Helpers;

namespace Broker.Tests.Services;

/// <summary>
/// AuditService.VerifyTraceIntegrity 回歸測試。
///
/// 鎖住 2026-05-14 修的 bug：DateTime.Kind round-trip 導致 hash 算錯。
/// 寫入時 Kind=Utc 帶 Z、SQLite 讀回 Kind=Unspecified 不帶 Z、format 出來不同字串。
/// 修法是 verify 端 SpecifyKind(Utc) 對齊寫入。
///
/// 這條 test 是 always-on guardian —— 任何「優化」把 SpecifyKind 拿掉、會立刻 0/3 valid。
/// 跟 AuditChainVerifierAgent 第一輪上線抓到 bug 的故事互補：
///   agent 抓 production 跑著的紀錄、test 抓 dev/CI 階段的回歸。
/// </summary>
public class AuditServiceVerifyTraceIntegrityTests : IDisposable
{
    private readonly BrokerDb _db;
    private readonly AuditService _svc;

    public AuditServiceVerifyTraceIntegrityTests()
    {
        _db = TestDb.CreateInMemory();
        _svc = new AuditService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void VerifyTraceIntegrity_FreshlyWrittenChain_ReturnsTrue()
    {
        // 模擬正常寫入：RecordEvent 多次（建立一條 trace）
        var traceId = "trace-test-001";
        _svc.RecordEvent(traceId, "API_REQUEST",  "prn_t", "task-1", null, "/x");
        _svc.RecordEvent(traceId, "DISPATCH_OK",  "prn_t", "task-1", null, "/x");
        _svc.RecordEvent(traceId, "API_RESPONSE", "prn_t", "task-1", null, "/x");

        // 從 DB 讀回（這時 DateTime.Kind 變成 Unspecified、之前就是這裡 fail）
        _svc.VerifyTraceIntegrity(traceId).Should().BeTrue(
            "DateTime.Kind round-trip 過後仍應 verify pass — fix 是 SpecifyKind(Utc) 對齊寫入");
    }

    [Fact]
    public void VerifyTraceIntegrity_EmptyTrace_ReturnsTrue()
    {
        // 空 trace 視為完整（沒事件可破壞鏈）
        _svc.VerifyTraceIntegrity("trace-not-exists").Should().BeTrue();
    }

    [Fact]
    public void VerifyTraceIntegrity_SingleEvent_ReturnsTrue()
    {
        var traceId = "trace-single";
        _svc.RecordEvent(traceId, "ONLY_EVENT", "prn_t");
        _svc.VerifyTraceIntegrity(traceId).Should().BeTrue();
    }

    [Fact]
    public void VerifyTraceIntegrity_TamperedEventHash_ReturnsFalse()
    {
        var traceId = "trace-tampered-hash";
        _svc.RecordEvent(traceId, "E1", "prn_t");
        _svc.RecordEvent(traceId, "E2", "prn_t");

        // 直接改 DB 模擬篡改 — UPDATE 改 event_hash
        var rows = _db.Execute(
            "UPDATE audit_events SET event_hash = 'tampered_hash_value' WHERE trace_id = @tid AND trace_seq = 1",
            new { tid = traceId });
        rows.Should().Be(1);

        _svc.VerifyTraceIntegrity(traceId).Should().BeFalse(
            "篡改 event_hash 後 verify 應該抓到、鏈完整性是 hash chain 設計的核心保證");
    }

    [Fact]
    public void VerifyTraceIntegrity_TamperedDetails_ReturnsFalse()
    {
        // 篡改 details 但沒改 payload_digest / event_hash — verify 不會直接抓 details
        // 但若改 payload_digest 卻沒改 event_hash 就會破壞 hash chain
        var traceId = "trace-payload-tampered";
        _svc.RecordEvent(traceId, "ORIG", "prn_t", details: "{\"x\":1}");
        _svc.RecordEvent(traceId, "NEXT", "prn_t");

        // 改 payload_digest — 模擬 details 被改但 hash 沒重算
        var rows = _db.Execute(
            "UPDATE audit_events SET payload_digest = 'fake_digest' WHERE trace_id = @tid AND trace_seq = 0",
            new { tid = traceId });
        rows.Should().Be(1);

        _svc.VerifyTraceIntegrity(traceId).Should().BeFalse();
    }

    [Fact]
    public void VerifyTraceIntegrity_BrokenChainLink_ReturnsFalse()
    {
        // 篡改 previous_event_hash 模擬「插入假事件」攻擊
        var traceId = "trace-broken-link";
        _svc.RecordEvent(traceId, "E1", "prn_t");
        _svc.RecordEvent(traceId, "E2", "prn_t");
        _svc.RecordEvent(traceId, "E3", "prn_t");

        var rows = _db.Execute(
            "UPDATE audit_events SET previous_event_hash = 'FAKE_PREV' WHERE trace_id = @tid AND trace_seq = 1",
            new { tid = traceId });
        rows.Should().Be(1);

        _svc.VerifyTraceIntegrity(traceId).Should().BeFalse();
    }

    [Fact]
    public void VerifyTraceIntegrity_OutOfOrderSeq_ReturnsFalse()
    {
        // 篡改 trace_seq、跳號（0,1,3 而非 0,1,2）
        var traceId = "trace-seq-gap";
        _svc.RecordEvent(traceId, "E1", "prn_t");
        _svc.RecordEvent(traceId, "E2", "prn_t");
        _svc.RecordEvent(traceId, "E3", "prn_t");

        _db.Execute(
            "UPDATE audit_events SET trace_seq = 99 WHERE trace_id = @tid AND trace_seq = 2",
            new { tid = traceId });

        _svc.VerifyTraceIntegrity(traceId).Should().BeFalse();
    }

    [Fact]
    public void VerifyTraceIntegrity_MultipleTracesIndependent_BothValid()
    {
        // 多條 trace 互不干擾、各自完整
        _svc.RecordEvent("trace-a", "E", "p1");
        _svc.RecordEvent("trace-a", "E", "p1");
        _svc.RecordEvent("trace-b", "E", "p2");
        _svc.RecordEvent("trace-b", "E", "p2");

        _svc.VerifyTraceIntegrity("trace-a").Should().BeTrue();
        _svc.VerifyTraceIntegrity("trace-b").Should().BeTrue();
    }

    [Fact]
    public void VerifyTraceIntegrity_LongChain_StillValid()
    {
        // 寫 50 個事件、確認長鏈也通過（防 off-by-one bug）
        var traceId = "trace-long";
        for (int i = 0; i < 50; i++)
            _svc.RecordEvent(traceId, $"E{i}", "prn_t");

        _svc.VerifyTraceIntegrity(traceId).Should().BeTrue();
    }
}
