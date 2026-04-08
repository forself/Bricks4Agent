using System.Text.Json;
using BrokerCore.Contracts.Transport;

namespace Unit.Tests.Transport;

public class TransportContractSerializationTests
{
    [Fact]
    public void TransportQueryResponse_serializes_follow_up_shape()
    {
        var response = new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.NeedFollowUp,
            Answer = "請問你要查哪一天？",
            MissingFields = ["date"],
            FollowUp = new TransportFollowUp
            {
                Question = "請問你要查哪一天？",
                FollowUpToken = "token-1",
                Options =
                [
                    new TransportFollowUpOption { Id = "today", Label = "今天" },
                    new TransportFollowUpOption { Id = "tomorrow", Label = "明天" }
                ]
            }
        };

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"resultType\":\"need_follow_up\"");
        json.Should().Contain("\"missingFields\":[\"date\"]");
        json.Should().Contain("\"followUpToken\":\"token-1\"");
    }
}
