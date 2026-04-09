using System.Text.Json;
using TransportTdxWorker.Handlers;

namespace Unit.Tests.Transport;

public class TransportQueryHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_returns_range_answer_when_rail_query_lacks_date_but_contains_stations()
    {
        var handler = new TransportQueryHandler();
        var payload = """
                      {
                        "args": {
                          "transport_mode": "rail",
                          "user_query": "板橋到高雄的火車班次"
                        }
                      }
                      """;

        var result = await handler.ExecuteAsync("req-1", "transport_query", payload, "local", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("resultType").GetString().Should().Be("range_answer");
        doc.RootElement.GetProperty("normalizedQuery").GetProperty("origin").GetString().Should().Be("板橋");
        doc.RootElement.GetProperty("normalizedQuery").GetProperty("destination").GetString().Should().Be("高雄");
        doc.RootElement.GetProperty("missingFields").EnumerateArray().Select(x => x.GetString()).Should().Contain("date");
    }
}
