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
    private readonly TransportQueryContextResolver _contextResolver;
    private readonly TdxTransportProvider _provider;

    public string CapabilityId => "transport.query";

    public TransportQueryHandler()
        : this(
            new TransportQuerySufficiencyAnalyzer(),
            new TransportFollowUpBuilder(),
            new TransportRangeAnswerBuilder(),
            new TransportQueryContextResolver(),
            new TdxTransportProvider())
    {
    }

    public TransportQueryHandler(
        TransportQuerySufficiencyAnalyzer analyzer,
        TransportFollowUpBuilder followUpBuilder,
        TransportRangeAnswerBuilder rangeAnswerBuilder,
        TransportQueryContextResolver contextResolver,
        TdxTransportProvider provider)
    {
        _analyzer = analyzer;
        _followUpBuilder = followUpBuilder;
        _rangeAnswerBuilder = rangeAnswerBuilder;
        _contextResolver = contextResolver;
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
        var root = doc.RootElement.TryGetProperty("args", out var argsElement) ? argsElement : doc.RootElement;
        var mode = root.TryGetProperty("transport_mode", out var modeElement) ? modeElement.GetString() ?? "auto" : "auto";
        var userQuery = root.TryGetProperty("user_query", out var queryElement) ? queryElement.GetString() ?? string.Empty : string.Empty;
        var context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("context", out var contextElement) && contextElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in contextElement.EnumerateObject())
            {
                context[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.GetString();
            }
        }

        var resolvedContext = _contextResolver.Resolve(mode, userQuery, context);
        var verdict = _analyzer.Analyze(mode, userQuery, resolvedContext);
        var response = verdict.State switch
        {
            TransportQueryState.Insufficient => BuildFollowUpResponse(verdict),
            TransportQueryState.PartiallySufficient => BuildRangeResponse(mode, verdict),
            _ => _provider.QueryAsync(verdict.NormalizedQuery, ct).GetAwaiter().GetResult()
        };

        return Task.FromResult<(bool Success, string? ResultPayload, string? Error)>(
            (true, JsonSerializer.Serialize(response), null));
    }

    private TransportQueryResponse BuildFollowUpResponse(TransportQueryVerdict verdict)
    {
        return new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.NeedFollowUp,
            Answer = "我還需要補一些資訊，才能精準查詢交通資料。",
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

    private TransportQueryResponse BuildRangeResponse(string mode, TransportQueryVerdict verdict)
    {
        var records = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["mode"] = mode,
                ["origin"] = verdict.NormalizedQuery.GetValueOrDefault("origin"),
                ["destination"] = verdict.NormalizedQuery.GetValueOrDefault("destination"),
                ["date"] = "nearest_available"
            }
        };

        var response = _rangeAnswerBuilder.Build(verdict, records);
        response.Evidence =
        [
            new Dictionary<string, string>
            {
                ["source"] = "TDX",
                ["kind"] = "transport.provider"
            }
        ];
        response.ProviderMetadata["provider"] = "tdx";
        return response;
    }
}
