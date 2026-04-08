using BrokerCore.Contracts.Transport;

namespace TransportTdxWorker.Services;

public sealed class TdxTransportProvider
{
    public Task<TransportQueryResponse> QueryAsync(
        Dictionary<string, object?> normalizedQuery,
        CancellationToken cancellationToken = default)
    {
        var mode = normalizedQuery.TryGetValue("transport_mode", out var value)
            ? value?.ToString() ?? "auto"
            : "auto";

        var records = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["mode"] = mode,
                ["origin"] = normalizedQuery.GetValueOrDefault("origin"),
                ["destination"] = normalizedQuery.GetValueOrDefault("destination"),
                ["date"] = normalizedQuery.GetValueOrDefault("date"),
                ["note"] = "placeholder tdx provider result"
            }
        };

        var response = new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.FinalAnswer,
            Answer = "已根據 TDX 資料整理結果。",
            NormalizedQuery = normalizedQuery,
            Records = records,
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
                ["provider"] = "tdx",
                ["mode"] = mode
            }
        };

        return Task.FromResult(response);
    }
}
