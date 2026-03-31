using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Api;

public class ProjectInterviewReviewTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public ProjectInterviewReviewTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProjectInterview_ReviseCreatesNewVersionAndKeepsPriorGraph()
    {
        const string userId = "line-project-review";
        await _fixture.CompleteProjectInterviewToReviewAsync(userId);

        var firstReview = await _fixture.ReadProjectInterviewReviewAsync("line", userId);
        firstReview.CurrentVersion.Should().Be(1);
        firstReview.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.AwaitUserReview);

        await _fixture.SendHighLevelLineTextAsync("/revise", userId);

        var review = await _fixture.ReadProjectInterviewReviewAsync("line", userId);
        review.CurrentVersion.Should().Be(2);
        review.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.AwaitUserReview);

        var firstDag = await _fixture.ReadProjectInterviewVersionDagAsync("line", userId, 1);
        var secondDag = await _fixture.ReadProjectInterviewVersionDagAsync("line", userId, 2);
        firstDag.Should().NotBeNull();
        secondDag.Should().NotBeNull();
    }
}
