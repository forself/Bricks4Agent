using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TdxTransportProviderTests
{
    [Fact]
    public async Task QueryAsync_returns_tdx_evidence_and_final_answer_shape()
    {
        var handler = new FakeHttpMessageHandler(
            """{"access_token":"token-123","expires_in":3600}""",
            """
            [
              {
                "DailyTrainInfo": {
                  "TrainNo": "123",
                  "TrainTypeName": { "Zh_tw": "自強(3000)" }
                },
                "StopTimes": [
                  {
                    "StationID": "1020",
                    "StopSequence": 5,
                    "DepartureTime": "09:10"
                  },
                  {
                    "StationID": "4400",
                    "StopSequence": 12,
                    "ArrivalTime": "13:20"
                  }
                ]
              }
            ]
            """);
        var api = new TdxApiService(
            new TdxOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                AuthUrl = "https://example.test/token",
                BaseUrl = "https://example.test/api/basic"
            },
            new HttpClient(handler),
            NullLogger<TdxApiService>.Instance);
        var provider = new TdxTransportProvider(api, NullLogger<TdxTransportProvider>.Instance);

        var response = await provider.QueryAsync(new Dictionary<string, object?>
        {
            ["transport_mode"] = "rail",
            ["origin"] = "板橋",
            ["destination"] = "高雄",
            ["date"] = "2026-04-10"
        });

        response.ResultType.Should().Be("final_answer");
        response.Evidence.Should().ContainSingle(x => x["source"] == "TDX");
        response.ProviderMetadata["provider"].Should().Be("tdx");
        response.Records.Should().NotBeEmpty();
        response.Answer.Should().Contain("板橋");
        response.Answer.Should().Contain("高雄");
        response.Answer.Should().Contain("09:10");
        response.Answer.Should().Contain("13:20");
    }

    private sealed class FakeHttpMessageHandler(string tokenJson, string dataJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsoluteUri.Contains("/token", StringComparison.OrdinalIgnoreCase)
                ? tokenJson
                : dataJson;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
