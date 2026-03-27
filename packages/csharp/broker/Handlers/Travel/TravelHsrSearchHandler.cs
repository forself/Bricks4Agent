using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Travel;

public sealed class TravelHsrSearchHandler : IRouteHandler
{
    public string Route => "travel_hsr_search";

    private readonly ILogger<TravelHsrSearchHandler> _logger;

    public TravelHsrSearchHandler(ILogger<TravelHsrSearchHandler> logger)
    {
        _logger = logger;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
        => TravelSearchHelper.ExecuteTravelSearchAsync(
            request,
            mode: "hsr",
            sourceLabel: "DuckDuckGo / thsrc.com.tw",
            queryDecorator: query => $"{query} site:thsrc.com.tw 高鐵 時刻表 班次",
            _logger);
}
