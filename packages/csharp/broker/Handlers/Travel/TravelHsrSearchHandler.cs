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
        var query = PayloadHelper.TryGetString(args, "query") ?? "";

        if (_tdxApiService is { IsConfigured: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var tdxResult = await TdxTravelHelper.QueryThsrTimetableAsync(_tdxApiService, query, _logger, ct);
                if (tdxResult != null)
                {
                    _logger.LogInformation("TDX THSR timetable query succeeded for: {Query}", query);
                    return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
                    {
                        mode = "hsr",
                        query,
                        retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                        tdx = tdxResult,
                        sources_used = new[] { "TDX 高鐵時刻表 API" }
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TDX THSR query failed, falling back to web search");
            }
        }

        return await TravelSearchHelper.ExecuteTravelSearchAsync(
            request,
            mode: "hsr",
            sourceLabel: "DuckDuckGo / thsrc.com.tw",
            queryDecorator: q => $"{q} site:thsrc.com.tw 高鐵 時刻表 班次",
            _logger);
    }
}
