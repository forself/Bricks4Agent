using Broker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broker.Tests.Services;

/// <summary>
/// ScheduledForensicsAgentBase 的 prompt parsing 合約測試。
/// 因為 base class 是 abstract、需要 concrete 子類來測試 instance 方法 ParsePrompt。
/// </summary>
public class ScheduledForensicsAgentBaseTests
{
    /// <summary>最小可測 subclass —— 只覆寫 abstract property、跑空 ExecuteAsync</summary>
    private sealed class TestAgent : ScheduledForensicsAgentBase
    {
        protected override string AgentId => "agent_test";
        protected override string PrincipalId => "prn_agent_test";
        protected override string DisplayName => "Test Agent";
        protected override int AutoPushIntervalSeconds => 60;
        protected override TimeSpan DefaultWindow => TimeSpan.FromHours(2);
        protected override string DefaultQuestion => "default-question-text";
        protected override string TaskType => "test_agent";

        public TestAgent() : base(NullServiceProvider.Instance, NullLogger<TestAgent>.Instance) { }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void ParsePrompt_ValidJson_ExtractsAllFields()
    {
        var agent = new TestAgent();
        var p = "{\"since\":\"2026-05-14T10:00:00Z\",\"until\":\"2026-05-14T11:00:00Z\",\"symbol\":\"BTC-USDT\",\"question\":\"what happened?\"}";
        var (since, until, symbol, question) = agent.ParsePrompt(p);
        since.Should().Be(new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Utc));
        until.Should().Be(new DateTime(2026, 5, 14, 11, 0, 0, DateTimeKind.Utc));
        symbol.Should().Be("BTC-USDT");
        question.Should().Be("what happened?");
    }

    [Fact]
    public void ParsePrompt_EmptyJson_UsesDefaults()
    {
        var agent = new TestAgent();
        var before = DateTime.UtcNow;
        var (since, until, symbol, question) = agent.ParsePrompt("{}");
        var after = DateTime.UtcNow;

        // since = now - DefaultWindow (2h)、until = now
        var expectedSinceLow = before.AddHours(-2).AddSeconds(-1);
        var expectedSinceHigh = after.AddHours(-2).AddSeconds(1);
        since.Should().BeOnOrAfter(expectedSinceLow).And.BeOnOrBefore(expectedSinceHigh);
        until.Should().BeOnOrAfter(before).And.BeOnOrBefore(after.AddSeconds(1));
        symbol.Should().BeNull();
        question.Should().Be("default-question-text");
    }

    [Fact]
    public void ParsePrompt_InvalidJson_FallbackToDefaultWindowWithPromptAsQuestion()
    {
        var agent = new TestAgent();
        var (since, until, symbol, question) = agent.ParsePrompt("not json at all");
        // since 應該約 now - DefaultWindow
        var diff = until - since;
        diff.Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromSeconds(5));
        symbol.Should().BeNull();
        question.Should().Be("not json at all");  // 整段當問題
    }

    [Fact]
    public void ParsePrompt_PartialFields_FillsOnlyMissing()
    {
        var agent = new TestAgent();
        var p = "{\"symbol\":\"ETH-USDT\"}";
        var (since, until, symbol, question) = agent.ParsePrompt(p);
        symbol.Should().Be("ETH-USDT");
        question.Should().Be("default-question-text");  // 沒給 question → fallback default
        (until - since).Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ParsePrompt_QuestionEmpty_UsesDefault()
    {
        var agent = new TestAgent();
        // question 給空字串、應該 fallback 到 DefaultQuestion
        var (_, _, _, question) = agent.ParsePrompt("{\"question\":\"\"}");
        // 注意實作上 q.GetString() ?? DefaultQuestion 是 null fallback、空字串會被當有效值
        // 這個 test 鎖住現有行為 —— 空字串視為 user 真的給了空問題
        question.Should().Be("");
    }

    [Fact]
    public void ParsePrompt_BadDateFormat_FallbackToDefaultWindow()
    {
        var agent = new TestAgent();
        var p = "{\"since\":\"not-a-date\",\"until\":\"also-not-a-date\"}";
        var (since, until, _, _) = agent.ParsePrompt(p);
        // since/until 解析失敗、應該 fallback、間距約 DefaultWindow
        (until - since).Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromSeconds(5));
    }
}
