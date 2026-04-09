using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Travel;

public sealed class TravelRailSearchHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "travel_rail_search";

    private readonly ILogger<TravelRailSearchHandler> _logger;
    private readonly TdxApiService? _tdxApiService;

    public TravelRailSearchHandler(ILogger<TravelRailSearchHandler> logger, TdxApiService? tdxApiService = null)
    {
        _logger = logger;
        _tdxApiService = tdxApiService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? string.Empty;
        const string sourceLabel = "TDX 台鐵時刻表 API";

        if (_tdxApiService is { IsConfigured: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var tdxResult = await TdxTravelHelper.QueryTraTimetableAsync(_tdxApiService, query, _logger, ct);
                if (tdxResult != null)
                    return TravelTdxResponseHelper.CreateSuccess(request.RequestId, "rail", query, tdxResult, sourceLabel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TDX-only rail query failed");
            }
        }

        return TravelTdxResponseHelper.CreateEmpty(request.RequestId, "rail", query, sourceLabel);
    }
}
