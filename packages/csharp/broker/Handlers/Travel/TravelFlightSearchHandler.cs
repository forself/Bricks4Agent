using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Travel;

public sealed class TravelFlightSearchHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "travel_flight_search";

    private readonly ILogger<TravelFlightSearchHandler> _logger;
    private readonly TdxApiService? _tdxApiService;

    public TravelFlightSearchHandler(ILogger<TravelFlightSearchHandler> logger, TdxApiService? tdxApiService = null)
    {
        _logger = logger;
        _tdxApiService = tdxApiService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? string.Empty;
        const string sourceLabel = "TDX 航班即時資訊 API (FIDS)";

        if (_tdxApiService is { IsConfigured: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var tdxResult = await TdxTravelHelper.QueryFlightAsync(_tdxApiService, query, _logger, ct);
                if (tdxResult != null)
                    return TravelTdxResponseHelper.CreateSuccess(request.RequestId, "flight", query, tdxResult, sourceLabel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TDX-only flight query failed");
            }
        }

        return TravelTdxResponseHelper.CreateEmpty(request.RequestId, "flight", query, sourceLabel);
    }
}
