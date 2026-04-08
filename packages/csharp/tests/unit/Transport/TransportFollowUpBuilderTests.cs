using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TransportFollowUpBuilderTests
{
    [Fact]
    public void Build_for_date_missing_returns_date_question_and_options()
    {
        var builder = new TransportFollowUpBuilder();

        var result = builder.Build(["date"]);

        result.Question.Should().Be("請問你要查哪一天？");
        result.Options.Select(x => x.Id).Should().Contain(["today", "tomorrow", "custom_date", "nearest_available"]);
        result.FollowUpToken.Should().NotBeNullOrWhiteSpace();
    }
}
