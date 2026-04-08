using System.Text.Json;
using BrokerCore.Contracts.Transport;
using WorkerSdk;

namespace TransportTdxWorker.Handlers;

public sealed class TransportQueryHandler : ICapabilityHandler
{
    public string CapabilityId => "transport.query";

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId,
        string route,
        string payload,
        string scope,
        CancellationToken ct)
    {
        var response = new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.NeedFollowUp,
            Answer = "請問你要查哪一天？",
            MissingFields = ["date"],
            FollowUp = new TransportFollowUp
            {
                Question = "請問你要查哪一天？",
                FollowUpToken = Guid.NewGuid().ToString("N"),
                Options =
                [
                    new TransportFollowUpOption { Id = "today", Label = "今天" },
                    new TransportFollowUpOption { Id = "tomorrow", Label = "明天" }
                ]
            },
            Evidence =
            [
                new Dictionary<string, string>
                {
                    ["source"] = "TDX",
                    ["kind"] = "transport.provider"
                }
            ],
            ProviderMetadata = new Dictionary<string, object?>
            {
                ["provider"] = "tdx"
            }
        };

        return Task.FromResult<(bool Success, string? ResultPayload, string? Error)>(
            (true, JsonSerializer.Serialize(response), null));
    }
}
