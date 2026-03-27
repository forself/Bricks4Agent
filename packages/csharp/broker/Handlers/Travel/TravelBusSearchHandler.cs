using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;

namespace Broker.Handlers.Travel;

public sealed class TravelBusSearchHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "travel_bus_search";

    private readonly ILogger<TravelBusSearchHandler> _logger;

    public TravelBusSearchHandler(ILogger<TravelBusSearchHandler> logger)
    {
        _logger = logger;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
        => TravelSearchHelper.ExecuteTravelSearchAsync(
            request,
            mode: "bus",
            sourceLabel: "DuckDuckGo / public transport web",
            queryDecorator: query => $"{query} 公車 OR 客運 時刻表 班次",
            _logger);
}
