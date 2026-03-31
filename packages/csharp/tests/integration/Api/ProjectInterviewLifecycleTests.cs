using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Api;

public class ProjectInterviewLifecycleTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public ProjectInterviewLifecycleTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProjectInterview_DoesNotPromoteAssertionsWithoutExplicitConfirmation()
    {
        const string userId = "line-project-lifecycle";
        var projectName = $"#AlphaPortalLifecycle{Guid.NewGuid():N}";
        using var start = await _fixture.SendHighLevelLineTextAsync("/proj", userId);
        start.RootElement.GetProperty("data").GetProperty("reply").GetString().Should().Contain("專案訪談已開始");
        start.RootElement.GetProperty("data").GetProperty("reply").GetString().Should().Contain("Project interview started");

        await _fixture.SendHighLevelLineTextAsync(projectName, userId);
        await _fixture.SendHighLevelLineTextAsync("I want an internal admin tool with login.", userId);

        var state = await _fixture.ReadProjectInterviewRequirementsAsync("line", userId);

        state.Assertions.Should().NotContain(assertion => assertion.Status == Broker.Services.AssertionStatus.Confirmed);
        state.PendingOptions.Should().NotBeEmpty();
        state.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.ClassifyProjectScale);
    }

    [Fact]
    public async Task ProjectInterview_RequiresExplicitProjectNameInBilingualReply()
    {
        const string userId = "line-project-name-bilingual";
        await _fixture.SendHighLevelLineTextAsync("/proj", userId);

        using var reply = await _fixture.SendHighLevelLineTextAsync("我要做一個內部系統", userId);
        var message = reply.RootElement.GetProperty("data").GetProperty("reply").GetString();

        message.Should().Contain("請先用 #專案名稱 回覆");
        message.Should().Contain("Reply with #ProjectName first");
    }
}
