using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Travel;

public sealed class TravelRailSearchHandler : IRouteHandler
{
    public string Route => "travel_rail_search";

    private readonly ILogger<TravelRailSearchHandler> _logger;

    public TravelRailSearchHandler(ILogger<TravelRailSearchHandler> logger)
    {
        _logger = logger;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
        => TravelSearchHelper.ExecuteTravelSearchAsync(
            request,
            mode: "rail",
            sourceLabel: "DuckDuckGo / railway.gov.tw",
            queryDecorator: query => $"{query} site:railway.gov.tw 火車 台鐵 時刻表 班次",
            _logger);
}
