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
        var query = PayloadHelper.TryGetString(args, "query") ?? "";

        // 優先嘗試 TDX API
        if (_tdxApiService is { IsConfigured: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var tdxResult = await TdxTravelHelper.QueryTraTimetableAsync(_tdxApiService, query, _logger, ct);
                if (tdxResult != null)
                {
                    _logger.LogInformation("TDX TRA timetable query succeeded for: {Query}", query);
                    return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
                    {
                        mode = "rail",
                        query,
                        retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                        tdx = tdxResult,
                        sources_used = new[] { "TDX 台鐵時刻表 API" }
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TDX TRA query failed, falling back to web search");
            }
        }

        // Fallback: 原有的 DuckDuckGo 搜尋
        return await TravelSearchHelper.ExecuteTravelSearchAsync(
            request,
            mode: "rail",
            sourceLabel: "DuckDuckGo / railway.gov.tw",
            queryDecorator: q => $"{q} site:railway.gov.tw 火車 台鐵 時刻表 班次",
            _logger);
    }
}
