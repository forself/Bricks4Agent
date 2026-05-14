using Broker.Endpoints;
using static Broker.Endpoints.ForensicsEndpoints;

namespace Broker.Tests.Endpoints;

/// <summary>
/// ForensicsEndpoints 的純函式測試（BuildLlmPrompts）。
/// BuildTimelineCore 需要 BrokerDb in-memory、留給 integration test 覆蓋。
/// </summary>
public class ForensicsEndpointsTests
{
    [Fact]
    public void BuildLlmPrompts_EmptyTimeline_StillProducesPrompts()
    {
        var summary = new TimelineSummary { AuditCount = 0, ApprovalCount = 0, LlmCount = 0 };
        var (sys, usr) = ForensicsEndpoints.BuildLlmPrompts(
            new List<ForensicsEndpoints.TimelineEvent>(), summary, "請說明發生了什麼");
        sys.Should().Contain("Bricks4Agent 平台的鑑識分析助手");
        sys.Should().Contain("不要編造事件");
        usr.Should().Contain("請說明發生了什麼");
        usr.Should().Contain("共 0 筆");
    }

    [Fact]
    public void BuildLlmPrompts_SystemPromptStableAcrossCalls()
    {
        // 系統 prompt 不應依輸入變化（避免幻覺 prompt injection）
        var summary = new TimelineSummary();
        var (sys1, _) = ForensicsEndpoints.BuildLlmPrompts(
            new List<ForensicsEndpoints.TimelineEvent>(), summary, "any");
        var (sys2, _) = ForensicsEndpoints.BuildLlmPrompts(
            new List<ForensicsEndpoints.TimelineEvent>(), summary, "different question");
        sys1.Should().Be(sys2);
    }

    [Fact]
    public void BuildLlmPrompts_UserPromptContainsCounts()
    {
        var summary = new TimelineSummary { AuditCount = 5, ApprovalCount = 2, LlmCount = 3, UniqueTraces = 4 };
        var (_, usr) = ForensicsEndpoints.BuildLlmPrompts(
            new List<ForensicsEndpoints.TimelineEvent>(), summary, "Q");
        usr.Should().Contain("audit=5");
        usr.Should().Contain("approvals=2");
        usr.Should().Contain("llm_reasoning=3");
        usr.Should().Contain("unique_traces=4");
    }

    [Fact]
    public void BuildLlmPrompts_TimelineEventsAreSerialized()
    {
        var summary = new TimelineSummary { AuditCount = 1 };
        var ev = new ForensicsEndpoints.TimelineEvent(
            new DateTime(2026, 5, 14, 10, 30, 45, DateTimeKind.Utc),
            "audit", "trace-001", "prn_test",
            "API_REQUEST · /api/v1/test",
            new { });
        var (_, usr) = ForensicsEndpoints.BuildLlmPrompts(
            new List<ForensicsEndpoints.TimelineEvent> { ev }, summary, "Q");
        usr.Should().Contain("[10:30:45]");          // time format
        usr.Should().Contain("audit");                // type
        usr.Should().Contain("API_REQUEST");          // summary
    }

    [Fact]
    public void BuildLlmPrompts_OnlyTakesFirst60Events()
    {
        var events = new List<ForensicsEndpoints.TimelineEvent>();
        for (int i = 0; i < 100; i++)
        {
            events.Add(new ForensicsEndpoints.TimelineEvent(
                DateTime.UtcNow.AddMinutes(-i),
                "audit", $"trace-{i}", "p",
                $"event-{i}",
                new { }));
        }
        var summary = new TimelineSummary { AuditCount = 100 };
        var (_, usr) = ForensicsEndpoints.BuildLlmPrompts(events, summary, "Q");
        // 「顯示前 60 筆」固定字串
        usr.Should().Contain("顯示前 60 筆");
        usr.Should().Contain("共 100 筆");
        // event-0 ~ event-59 應該在、event-60+ 不在
        usr.Should().Contain("event-0\n");
        usr.Should().Contain("event-59");
        usr.Should().NotContain("event-99");
    }
}
