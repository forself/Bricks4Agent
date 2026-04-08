using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TransportQuerySufficiencyAnalyzerTests
{
    [Fact]
    public void Rail_query_without_origin_or_destination_is_insufficient()
    {
        var analyzer = new TransportQuerySufficiencyAnalyzer();
        var verdict = analyzer.Analyze("rail", "幫我查火車", new Dictionary<string, string?>());

        verdict.State.Should().Be(TransportQueryState.Insufficient);
        verdict.MissingFields.Should().Contain(["origin", "destination"]);
    }

    [Fact]
    public void Rail_query_without_date_is_partially_sufficient()
    {
        var analyzer = new TransportQuerySufficiencyAnalyzer();
        var verdict = analyzer.Analyze("rail", "板橋到高雄的火車", new Dictionary<string, string?>
        {
            ["origin"] = "板橋",
            ["destination"] = "高雄"
        });

        verdict.State.Should().Be(TransportQueryState.PartiallySufficient);
        verdict.MissingFields.Should().Contain("date");
    }
}
