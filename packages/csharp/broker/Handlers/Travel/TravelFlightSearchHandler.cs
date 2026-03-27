using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Travel;

public sealed class TravelFlightSearchHandler : IRouteHandler
{
    public string Route => "travel_flight_search";

    private readonly ILogger<TravelFlightSearchHandler> _logger;

    public TravelFlightSearchHandler(ILogger<TravelFlightSearchHandler> logger)
    {
        _logger = logger;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
        => TravelSearchHelper.ExecuteTravelSearchAsync(
            request,
            mode: "flight",
            sourceLabel: "DuckDuckGo / public travel web",
            queryDecorator: query => $"{query} 航班 班次 時刻表",
            _logger);
}
