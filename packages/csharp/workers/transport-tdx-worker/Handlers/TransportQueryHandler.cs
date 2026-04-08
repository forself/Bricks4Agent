using System.Text.Json;
using BrokerCore.Contracts.Transport;
using TransportTdxWorker.Services;
using WorkerSdk;

namespace TransportTdxWorker.Handlers;

public sealed class TransportQueryHandler : ICapabilityHandler
{
    private readonly TransportQuerySufficiencyAnalyzer _analyzer;
    private readonly TransportFollowUpBuilder _followUpBuilder;
    private readonly TransportRangeAnswerBuilder _rangeAnswerBuilder;
    private readonly TdxTransportProvider _provider;

    public string CapabilityId => "transport.query";

    public TransportQueryHandler()
        : this(
            new TransportQuerySufficiencyAnalyzer(),
            new TransportFollowUpBuilder(),
            new TransportRangeAnswerBuilder(),
            new TdxTransportProvider())
    {
    }

    public TransportQueryHandler(
        TransportQuerySufficiencyAnalyzer analyzer,
        TransportFollowUpBuilder followUpBuilder,
        TransportRangeAnswerBuilder rangeAnswerBuilder,
        TdxTransportProvider provider)
    {
        _analyzer = analyzer;
        _followUpBuilder = followUpBuilder;
        _rangeAnswerBuilder = rangeAnswerBuilder;
        _provider = provider;
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId,
        string route,
        string payload,
        string scope,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement.TryGetProperty("args", out var argsEl) ? argsEl : doc.RootElement;
        var mode = root.TryGetProperty("transport_mode", out var modeEl) ? modeEl.GetString() ?? "auto" : "auto";
        var userQuery = root.TryGetProperty("user_query", out var queryEl) ? queryEl.GetString() ?? string.Empty : string.Empty;
        var context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("context", out var contextEl) && contextEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in contextEl.EnumerateObject())
            {
                context[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.GetString();
            }
        }

        var verdict = _analyzer.Analyze(mode, userQuery, context);
        TransportQueryResponse response;

        if (verdict.State == TransportQueryState.Insufficient)
        {
            response = new TransportQueryResponse
            {
                ResultTypeValue = TransportResultType.NeedFollowUp,
                Answer = "目前資訊不足，我需要再確認一些條件。",
                MissingFields = verdict.MissingFields,
                NormalizedQuery = verdict.NormalizedQuery,
                FollowUp = _followUpBuilder.Build(verdict.MissingFields),
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
        }
        else if (verdict.State == TransportQueryState.PartiallySufficient)
        {
            response = _rangeAnswerBuilder.Build(
                verdict,
                [
                    new Dictionary<string, object?>
                    {
                        ["mode"] = mode,
                        ["origin"] = verdict.NormalizedQuery.GetValueOrDefault("origin"),
                        ["destination"] = verdict.NormalizedQuery.GetValueOrDefault("destination"),
                        ["date"] = "nearest_available"
                    }
                ]);
            response.Evidence =
            [
                new Dictionary<string, string>
                {
                    ["source"] = "TDX",
                    ["kind"] = "transport.provider"
                }
            ];
            response.ProviderMetadata["provider"] = "tdx";
        }
        else
        {
            response = _provider.QueryAsync(verdict.NormalizedQuery, ct).GetAwaiter().GetResult();
        }

        return Task.FromResult<(bool Success, string? ResultPayload, string? Error)>(
            (true, JsonSerializer.Serialize(response), null));
    }
}
