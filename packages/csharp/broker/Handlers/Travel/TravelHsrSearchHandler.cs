using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Travel;

public sealed class TravelHsrSearchHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "travel_hsr_search";

    private readonly ILogger<TravelHsrSearchHandler> _logger;
    private readonly TdxApiService? _tdxApiService;

    public TravelHsrSearchHandler(ILogger<TravelHsrSearchHandler> logger, TdxApiService? tdxApiService = null)
    {
        _logger = logger;
        _tdxApiService = tdxApiService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? string.Empty;
        const string sourceLabel = "TDX 高鐵時刻表 API";

        if (_tdxApiService is { IsConfigured: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var tdxResult = await TdxTravelHelper.QueryThsrTimetableAsync(_tdxApiService, query, _logger, ct);
                if (tdxResult != null)
                    return TravelTdxResponseHelper.CreateSuccess(request.RequestId, "hsr", query, tdxResult, sourceLabel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TDX-only hsr query failed");
            }
        }

        return TravelTdxResponseHelper.CreateEmpty(request.RequestId, "hsr", query, sourceLabel);
    }
}
