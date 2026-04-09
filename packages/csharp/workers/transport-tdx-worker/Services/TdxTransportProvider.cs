using System.Text;
using System.Text.Json;
using BrokerCore.Contracts.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TransportTdxWorker.Services;

public sealed class TdxTransportProvider
{
    private readonly TdxApiService _apiService;
    private readonly TransportQueryContextResolver _resolver;
    private readonly ILogger<TdxTransportProvider> _logger;

    public TdxTransportProvider()
        : this(
            new TdxApiService(new TdxOptions(), new HttpClient(), NullLogger<TdxApiService>.Instance),
            new TransportQueryContextResolver(),
            NullLogger<TdxTransportProvider>.Instance)
    {
    }

    public TdxTransportProvider(TdxApiService apiService, ILogger<TdxTransportProvider> logger)
        : this(apiService, new TransportQueryContextResolver(), logger)
    {
    }

    public TdxTransportProvider(
        TdxApiService apiService,
        TransportQueryContextResolver resolver,
        ILogger<TdxTransportProvider> logger)
    {
        _apiService = apiService;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<TransportQueryResponse> QueryAsync(
        Dictionary<string, object?> normalizedQuery,
        CancellationToken cancellationToken = default)
    {
        var mode = normalizedQuery.TryGetValue("transport_mode", out var value)
            ? value?.ToString() ?? "auto"
            : "auto";

        return mode switch
        {
            "rail" => await QueryRailAsync(normalizedQuery, cancellationToken),
            "hsr" => await QueryHsrAsync(normalizedQuery, cancellationToken),
            "bus" => await QueryBusAsync(normalizedQuery, cancellationToken),
            "flight" => await QueryFlightAsync(normalizedQuery, cancellationToken),
            _ => CreateEmptyFinalAnswer(
                normalizedQuery,
                $"目前尚未支援 {mode} 類型的交通查詢。",
                records: [])
        };
    }

    private async Task<TransportQueryResponse> QueryRailAsync(
        Dictionary<string, object?> normalizedQuery,
        CancellationToken cancellationToken)
    {
        var origin = normalizedQuery.GetValueOrDefault("origin")?.ToString();
        var destination = normalizedQuery.GetValueOrDefault("destination")?.ToString();
        var originId = _resolver.GetTraStationId(origin);
        var destinationId = _resolver.GetTraStationId(destination);
        var date = _resolver.ParseDate(normalizedQuery.GetValueOrDefault("date")?.ToString());
        var timeRange = _resolver.ParseTimeRange(normalizedQuery.GetValueOrDefault("time_range")?.ToString());

        if (string.IsNullOrWhiteSpace(originId) || string.IsNullOrWhiteSpace(destinationId))
        {
            return CreateEmptyFinalAnswer(normalizedQuery, "目前無法辨識台鐵起訖站。", []);
        }

        var document = await _apiService.GetTraDailyTimetableByStationAsync(originId, date, cancellationToken);
        return BuildRailLikeResponse(
            normalizedQuery,
            origin ?? string.Empty,
            destination ?? string.Empty,
            "TDX 台鐵時刻表",
            document,
            originId,
            destinationId,
            timeRange);
    }

    private async Task<TransportQueryResponse> QueryHsrAsync(
        Dictionary<string, object?> normalizedQuery,
        CancellationToken cancellationToken)
    {
        var origin = normalizedQuery.GetValueOrDefault("origin")?.ToString();
        var destination = normalizedQuery.GetValueOrDefault("destination")?.ToString();
        var originId = _resolver.GetThsrStationId(origin);
        var destinationId = _resolver.GetThsrStationId(destination);
        var date = _resolver.ParseDate(normalizedQuery.GetValueOrDefault("date")?.ToString());
        var timeRange = _resolver.ParseTimeRange(normalizedQuery.GetValueOrDefault("time_range")?.ToString());

        if (string.IsNullOrWhiteSpace(originId) || string.IsNullOrWhiteSpace(destinationId))
        {
            return CreateEmptyFinalAnswer(normalizedQuery, "目前無法辨識高鐵起訖站。", []);
        }

        var document = await _apiService.GetThsrDailyTimetableAsync(date, cancellationToken);
        return BuildRailLikeResponse(
            normalizedQuery,
            origin ?? string.Empty,
            destination ?? string.Empty,
            "TDX 高鐵時刻表",
            document,
            originId,
            destinationId,
            timeRange);
    }

    private async Task<TransportQueryResponse> QueryBusAsync(
        Dictionary<string, object?> normalizedQuery,
        CancellationToken cancellationToken)
    {
        var cityLabel = normalizedQuery.GetValueOrDefault("city")?.ToString();
        var route = normalizedQuery.GetValueOrDefault("route")?.ToString();
        var tdxCity = _resolver.NormalizeCityToTdx(cityLabel);

        if (string.IsNullOrWhiteSpace(tdxCity) || string.IsNullOrWhiteSpace(route))
        {
            return CreateEmptyFinalAnswer(normalizedQuery, "目前無法辨識公車所在城市或路線。", []);
        }

        var document = await _apiService.GetCityBusEstimatedTimeAsync(tdxCity, route, cancellationToken);
        if (document == null)
        {
            return CreateEmptyFinalAnswer(normalizedQuery, $"目前沒有取得 {cityLabel} {route} 的公車到站資料。", []);
        }

        var records = new List<Dictionary<string, object?>>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var stopName = GetLocalizedText(item, "StopName");
            if (string.IsNullOrWhiteSpace(stopName))
            {
                continue;
            }

            var estimateMinutes = item.TryGetProperty("EstimateTime", out var estimateNode) &&
                                  estimateNode.ValueKind == JsonValueKind.Number &&
                                  estimateNode.TryGetInt32(out var seconds)
                ? Math.Max(0, (int)Math.Ceiling(seconds / 60d))
                : (int?)null;

            records.Add(new Dictionary<string, object?>
            {
                ["route_name"] = route,
                ["stop_name"] = stopName,
                ["estimate_minutes"] = estimateMinutes,
                ["direction"] = item.TryGetProperty("Direction", out var directionNode) && directionNode.TryGetInt32(out var direction)
                    ? (direction == 0 ? "去程" : direction == 1 ? "返程" : string.Empty)
                    : string.Empty
            });
        }

        var orderedRecords = records
            .OrderBy(record => record["estimate_minutes"] as int? ?? int.MaxValue)
            .Take(8)
            .ToList();

        if (orderedRecords.Count == 0)
        {
            return CreateEmptyFinalAnswer(normalizedQuery, $"目前沒有取得 {cityLabel} {route} 的公車到站資料。", []);
        }

        var answer = new StringBuilder()
            .AppendLine($"{cityLabel} {route} 公車到站資訊：")
            .AppendJoin(Environment.NewLine, orderedRecords.Select(record =>
            {
                var minutes = record["estimate_minutes"] is int estimate
                    ? $"{estimate} 分"
                    : "即將進站/未提供";
                var direction = record["direction"]?.ToString();
                return $"- {record["stop_name"]} {direction}：{minutes}";
            }))
            .ToString();

        return CreateFinalAnswer(
            normalizedQuery,
            answer,
            orderedRecords,
            "TDX",
            "transport.provider");
    }

    private async Task<TransportQueryResponse> QueryFlightAsync(
        Dictionary<string, object?> normalizedQuery,
        CancellationToken cancellationToken)
    {
        var origin = normalizedQuery.GetValueOrDefault("origin")?.ToString();
        var destination = normalizedQuery.GetValueOrDefault("destination")?.ToString();
        var originCode = _resolver.GetAirportCode(origin);
        var destinationCode = _resolver.GetAirportCode(destination);

        if (string.IsNullOrWhiteSpace(originCode) || string.IsNullOrWhiteSpace(destinationCode))
        {
            return CreateEmptyFinalAnswer(normalizedQuery, "目前無法辨識航班起訖機場。", []);
        }

        var document = await _apiService.GetDomesticFlightsAsync(originCode, cancellationToken);
        if (document == null)
        {
            return CreateEmptyFinalAnswer(normalizedQuery, "目前沒有取得可用的國內航班資料。", []);
        }

        var records = new List<Dictionary<string, object?>>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var depAirport = item.TryGetProperty("DepartureAirportID", out var depNode) ? depNode.GetString() : null;
            var arrAirport = item.TryGetProperty("ArrivalAirportID", out var arrNode) ? arrNode.GetString() : null;
            if (!string.Equals(depAirport, originCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(arrAirport, destinationCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var airlineId = item.TryGetProperty("AirlineID", out var airlineNode) ? airlineNode.GetString() ?? string.Empty : string.Empty;
            var flightNo = item.TryGetProperty("FlightNumber", out var noNode) ? noNode.GetString() ?? string.Empty : string.Empty;
            var departureTime = TrimIsoTime(item.TryGetProperty("ScheduleDepartureTime", out var departureNode) ? departureNode.GetString() : null);
            var arrivalTime = TrimIsoTime(item.TryGetProperty("ScheduleArrivalTime", out var arrivalNode) ? arrivalNode.GetString() : null);

            records.Add(new Dictionary<string, object?>
            {
                ["flight_no"] = $"{airlineId}{flightNo}",
                ["departure_time"] = departureTime,
                ["arrival_time"] = arrivalTime
            });
        }

        var ordered = records.Take(8).ToList();
        if (ordered.Count == 0)
        {
            return CreateEmptyFinalAnswer(normalizedQuery, "目前沒有取得可用的國內航班資料。", []);
        }

        var answer = new StringBuilder()
            .AppendLine($"{origin} 到 {destination} 航班：")
            .AppendJoin(Environment.NewLine, ordered.Select(record =>
                $"- {record["flight_no"]}：{record["departure_time"]} 起飛，{record["arrival_time"]} 抵達"))
            .ToString();

        return CreateFinalAnswer(
            normalizedQuery,
            answer,
            ordered,
            "TDX",
            "transport.provider");
    }

    private TransportQueryResponse BuildRailLikeResponse(
        Dictionary<string, object?> normalizedQuery,
        string origin,
        string destination,
        string sourceLabel,
        JsonDocument? document,
        string originId,
        string destinationId,
        (int startHour, int endHour)? timeRange)
    {
        if (document == null)
        {
            return CreateEmptyFinalAnswer(normalizedQuery, $"目前沒有取得 {origin} 到 {destination} 的班次資料。", []);
        }

        var records = new List<Dictionary<string, object?>>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("StopTimes", out var stopTimes) || stopTimes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            int originSequence = -1;
            int destinationSequence = -1;
            string departureTime = string.Empty;
            string arrivalTime = string.Empty;

            foreach (var stop in stopTimes.EnumerateArray())
            {
                var stationId = stop.TryGetProperty("StationID", out var stationNode) ? stationNode.GetString() : null;
                var sequence = stop.TryGetProperty("StopSequence", out var seqNode) && seqNode.TryGetInt32(out var value)
                    ? value
                    : -1;

                if (stationId == originId && originSequence < 0)
                {
                    originSequence = sequence;
                    departureTime = stop.TryGetProperty("DepartureTime", out var departureNode)
                        ? departureNode.GetString() ?? string.Empty
                        : string.Empty;
                }
                else if (stationId == destinationId && originSequence >= 0)
                {
                    destinationSequence = sequence;
                    arrivalTime = stop.TryGetProperty("ArrivalTime", out var arrivalNode)
                        ? arrivalNode.GetString() ?? string.Empty
                        : string.Empty;
                    break;
                }
            }

            if (originSequence < 0 || destinationSequence < 0 || originSequence >= destinationSequence)
            {
                continue;
            }

            if (timeRange.HasValue && !string.IsNullOrWhiteSpace(departureTime))
            {
                var hour = int.Parse(departureTime[..2]);
                if (hour < timeRange.Value.startHour || hour >= timeRange.Value.endHour)
                {
                    continue;
                }
            }

            var trainNumber = item.TryGetProperty("DailyTrainInfo", out var trainInfoNode) &&
                              trainInfoNode.TryGetProperty("TrainNo", out var trainNoNode)
                ? trainNoNode.GetString() ?? string.Empty
                : string.Empty;
            var trainType = item.TryGetProperty("DailyTrainInfo", out var infoNode) &&
                            infoNode.TryGetProperty("TrainTypeName", out var typeNode) &&
                            typeNode.TryGetProperty("Zh_tw", out var zhNode)
                ? zhNode.GetString() ?? string.Empty
                : string.Empty;

            records.Add(new Dictionary<string, object?>
            {
                ["train_no"] = trainNumber,
                ["train_type"] = trainType.Split('(')[0].Trim(),
                ["departure_time"] = departureTime,
                ["arrival_time"] = arrivalTime
            });
        }

        var ordered = records
            .OrderBy(record => record["departure_time"]?.ToString())
            .Take(8)
            .ToList();

        if (ordered.Count == 0)
        {
            return CreateEmptyFinalAnswer(normalizedQuery, $"目前沒有取得 {origin} 到 {destination} 的班次資料。", []);
        }

        var answer = new StringBuilder()
            .AppendLine($"{origin} 到 {destination} 班次：")
            .AppendJoin(Environment.NewLine, ordered.Select(record =>
            {
                var type = record["train_type"]?.ToString();
                var typePrefix = string.IsNullOrWhiteSpace(type) ? string.Empty : $"{type} ";
                return $"- {typePrefix}{record["train_no"]}：{record["departure_time"]} 發車，{record["arrival_time"]} 抵達";
            }))
            .ToString();

        return CreateFinalAnswer(
            normalizedQuery,
            answer,
            ordered,
            "TDX",
            "transport.provider",
            sourceLabel);
    }

    private static TransportQueryResponse CreateFinalAnswer(
        Dictionary<string, object?> normalizedQuery,
        string answer,
        IReadOnlyList<Dictionary<string, object?>> records,
        string evidenceSource,
        string evidenceKind,
        string? sourceLabel = null)
    {
        return new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.FinalAnswer,
            Answer = answer,
            NormalizedQuery = normalizedQuery,
            Records = records.ToList(),
            Evidence =
            [
                new Dictionary<string, string>
                {
                    ["source"] = evidenceSource,
                    ["kind"] = evidenceKind
                }
            ],
            ProviderMetadata = new Dictionary<string, object?>
            {
                ["provider"] = "tdx",
                ["source_label"] = sourceLabel ?? evidenceSource
            }
        };
    }

    private static TransportQueryResponse CreateEmptyFinalAnswer(
        Dictionary<string, object?> normalizedQuery,
        string answer,
        IReadOnlyList<Dictionary<string, object?>> records)
        => CreateFinalAnswer(normalizedQuery, answer, records, "TDX", "transport.provider");

    private static string GetLocalizedText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var node))
        {
            return string.Empty;
        }

        if (node.ValueKind == JsonValueKind.String)
        {
            return node.GetString() ?? string.Empty;
        }

        if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("Zh_tw", out var zhNode))
        {
            return zhNode.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string TrimIsoTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length >= 16 && value[10] == 'T'
            ? value[11..16]
            : value;
    }
}
