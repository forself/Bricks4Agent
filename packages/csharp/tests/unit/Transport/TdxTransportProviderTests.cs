using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TdxTransportProviderTests
{
    [Fact]
    public async Task QueryAsync_returns_tdx_evidence_and_final_answer_shape()
    {
        var provider = new TdxTransportProvider();

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
    }
}
