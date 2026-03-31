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
        await _fixture.SendHighLevelLineTextAsync("/proj");
        await _fixture.SendHighLevelLineTextAsync("#AlphaPortal");
        await _fixture.SendHighLevelLineTextAsync("I want an internal admin tool with login.");

        var state = await _fixture.ReadProjectInterviewRequirementsAsync("line", _fixture.DefaultLineUserId);

        state.Assertions.Should().NotContain(assertion => assertion.Status == Broker.Services.AssertionStatus.Confirmed);
        state.PendingOptions.Should().NotBeEmpty();
        state.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.ClassifyProjectScale);
    }
}
