using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TransportRangeAnswerBuilderTests
{
    [Fact]
    public void Build_creates_range_answer_with_assumption_disclosure()
    {
        var builder = new TransportRangeAnswerBuilder();
        var verdict = new TransportQueryVerdict
        {
            State = TransportQueryState.PartiallySufficient,
            MissingFields = ["date"],
            NormalizedQuery = new Dictionary<string, object?>
            {
                ["transport_mode"] = "rail",
                ["origin"] = "板橋",
                ["destination"] = "高雄"
            }
        };

        var response = builder.Build(verdict, []);

        response.ResultType.Should().Be("range_answer");
        response.MissingFields.Should().Contain("date");
        response.RangeContext.Should().NotBeNull();
        response.RangeContext!["scope_note"].Should().NotBeNull();
    }
}
