using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Travel;

public sealed class TravelBusSearchHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "travel_bus_search";

    private readonly ILogger<TravelBusSearchHandler> _logger;
    private readonly TdxApiService? _tdxApiService;

    public TravelBusSearchHandler(ILogger<TravelBusSearchHandler> logger, TdxApiService? tdxApiService = null)
    {
        _logger = logger;
        _tdxApiService = tdxApiService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? string.Empty;
        const string sourceLabel = "TDX 公車預估到站 API";

        if (_tdxApiService is { IsConfigured: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var tdxResult = await TdxBusTravelHelper.QueryBusAsync(_tdxApiService, query, _logger, ct);
                if (tdxResult != null)
                    return TravelTdxResponseHelper.CreateSuccess(request.RequestId, "bus", query, tdxResult, sourceLabel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TDX-only bus query failed");
            }
        }

        return TravelTdxResponseHelper.CreateEmpty(request.RequestId, "bus", query, sourceLabel);
    }
}
