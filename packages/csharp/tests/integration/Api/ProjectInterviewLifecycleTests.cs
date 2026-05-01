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

    [Fact]
    public async Task ProjectInterview_ProjectScalePrompt_HidesInternalScaleTerms()
    {
        const string userId = "line-project-scale-friendly";
        var projectName = $"#FriendlyScale{Guid.NewGuid():N}";

        await _fixture.SendHighLevelLineTextAsync("/proj", userId);
        using var scalePrompt = await _fixture.SendHighLevelLineTextAsync(projectName, userId);
        var message = scalePrompt.RootElement.GetProperty("data").GetProperty("reply").GetString();

        message.Should().Contain("請選一個最接近的規模");
        message.Should().Contain("Choose the closest scope");
        message.Should().NotContain("tool_page");
        message.Should().NotContain("mini_app");
        message.Should().NotContain("structured_app");
    }

    [Fact]
    public async Task ProjectInterview_TemplatePrompt_HidesInternalTemplateTerms()
    {
        const string userId = "line-project-template-friendly";
        var projectName = $"#FriendlyTemplate{Guid.NewGuid():N}";

        await _fixture.SendHighLevelLineTextAsync("/proj", userId);
        await _fixture.SendHighLevelLineTextAsync(projectName, userId);
        using var templatePrompt = await _fixture.SendHighLevelLineTextAsync("2", userId);
        var message = templatePrompt.RootElement.GetProperty("data").GetProperty("reply").GetString();

        message.Should().Contain("網站結構方向");
        message.Should().Contain("site structure direction");
        message.Should().NotContain("template family");
    }

    [Fact]
    public async Task ProjectInterview_ExpiredSessionFallsBackToConversationRoute()
    {
        const string userId = "line-project-expired-session";

        await _fixture.SendHighLevelLineTextAsync("/proj", userId);
        await _fixture.AgeProjectInterviewRequirementsAsync("line", userId, TimeSpan.FromHours(2));

        using var response = await _fixture.SendHighLevelLineTextAsync("hello", userId);
        var data = response.RootElement.GetProperty("data");

        data.GetProperty("mode").GetString().Should().Be("conversation");
        data.GetProperty("decision_reason").GetString().Should().Be("default conversation route");

        var state = await _fixture.ReadProjectInterviewRequirementsAsync("line", userId);
        state.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.Expired);
        state.IsActiveSession.Should().BeFalse();
    }
}
