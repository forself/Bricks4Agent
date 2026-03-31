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
        using var reviewReady = await _fixture.CompleteProjectInterviewToReviewAsync(userId);
        var reviewReadyMessage = reviewReady.RootElement.GetProperty("data").GetProperty("reply").GetString();
        reviewReadyMessage.Should().Contain("審查文件");
        reviewReadyMessage.Should().Contain("Review artifacts");

        var firstReview = await _fixture.ReadProjectInterviewReviewAsync("line", userId);
        firstReview.CurrentVersion.Should().Be(1);
        firstReview.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.AwaitUserReview);

        using var revise = await _fixture.SendHighLevelLineTextAsync("/revise", userId);
        var reviseMessage = revise.RootElement.GetProperty("data").GetProperty("reply").GetString();
        reviseMessage.Should().Contain("修訂版本");
        reviseMessage.Should().Contain("Revision draft v2");

        var review = await _fixture.ReadProjectInterviewReviewAsync("line", userId);
        review.CurrentVersion.Should().Be(2);
        review.SessionState.CurrentPhase.Should().Be(Broker.Services.ProjectInterviewPhase.AwaitUserReview);

        var firstDag = await _fixture.ReadProjectInterviewVersionDagAsync("line", userId, 1);
        var secondDag = await _fixture.ReadProjectInterviewVersionDagAsync("line", userId, 2);
        firstDag.Should().NotBeNull();
        secondDag.Should().NotBeNull();
    }
}
