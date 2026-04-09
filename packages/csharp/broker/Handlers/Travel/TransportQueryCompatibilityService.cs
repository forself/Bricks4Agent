using System.Text.Json;
using BrokerCore.Contracts.Transport;
using Broker.Services;

namespace Broker.Handlers.Travel;

internal static class TransportQueryCompatibilityService
{
    public static async Task<TransportQueryResponse> ExecuteAsync(
        string mode,
        string userQuery,
        IDictionary<string, string?> context,
        TdxApiService? tdxApiService,
        ILogger logger,
        CancellationToken ct)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "auto" : mode.Trim().ToLowerInvariant();
        var normalizedQuery = BuildNormalizedQuery(normalizedMode, userQuery, context);

        var missingFields = GetMissingFields(normalizedMode, normalizedQuery);
        if (missingFields.Count > 0)
        {
            return new TransportQueryResponse
            {
                ResultTypeValue = TransportResultType.NeedFollowUp,
                Answer = BuildNeedFollowUpAnswer(normalizedMode, missingFields),
                MissingFields = missingFields,
                NormalizedQuery = ToObjectDictionary(normalizedQuery),
                FollowUp = BuildFollowUp(normalizedMode, missingFields),
                Evidence =
                [
                    new Dictionary<string, string>
                    {
                        ["source"] = "TDX",
                        ["kind"] = "transport.provider"
                    }
                ],
                ProviderMetadata = new Dictionary<string, object?>
                {
                    ["provider"] = "tdx",
                    ["mode"] = normalizedMode,
                    ["compatibility_fallback"] = true
                }
            };
        }

        var missingDate = RequiresDate(normalizedMode) && string.IsNullOrWhiteSpace(normalizedQuery.GetValueOrDefault("date"));
        object? rawTdxResult = null;
        string sourceLabel = GetSourceLabel(normalizedMode);

        if (tdxApiService is { IsConfigured: true })
        {
            rawTdxResult = await QueryProviderAsync(tdxApiService, normalizedMode, userQuery, logger, ct);
        }

        var answer = BuildAnswer(normalizedMode, userQuery, rawTdxResult, sourceLabel, missingDate);
        var resultType = missingDate ? TransportResultType.RangeAnswer : TransportResultType.FinalAnswer;

        var response = new TransportQueryResponse
        {
            ResultTypeValue = resultType,
            Answer = answer,
            NormalizedQuery = ToObjectDictionary(normalizedQuery),
            MissingFields = missingDate ? ["date"] : [],
            Evidence =
            [
                new Dictionary<string, string>
                {
                    ["source"] = "TDX",
                    ["kind"] = "transport.provider"
                }
            ],
            ProviderMetadata = new Dictionary<string, object?>
            {
                ["provider"] = "tdx",
                ["mode"] = normalizedMode,
                ["compatibility_fallback"] = true
            }
        };

        if (missingDate)
        {
            response.RangeContext = new Dictionary<string, object?>
            {
                ["assumptions"] = new[] { "date=today" },
                ["scope_note"] = "目前先以今天可查到的結果提供範圍整理。"
            };
        }

        return response;
    }

    private static Dictionary<string, string?> BuildNormalizedQuery(
        string mode,
        string userQuery,
        IDictionary<string, string?> context)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["transport_mode"] = mode,
            ["origin"] = Get(context, "origin"),
            ["destination"] = Get(context, "destination"),
            ["date"] = Get(context, "date"),
            ["time_range"] = Get(context, "time_range"),
            ["city"] = Get(context, "city"),
            ["route"] = Get(context, "route")
        };

        switch (mode)
        {
            case "rail":
                MergeRailQuery(normalized, userQuery, useTra: true);
                break;
            case "hsr":
                MergeRailQuery(normalized, userQuery, useTra: false);
                break;
            case "flight":
                MergeFlightQuery(normalized, userQuery);
                break;
            case "bus":
                MergeBusQuery(normalized, userQuery);
                break;
        }

        return normalized;
    }

    private static void MergeRailQuery(Dictionary<string, string?> normalized, string userQuery, bool useTra)
    {
        if (string.IsNullOrWhiteSpace(normalized["origin"]) || string.IsNullOrWhiteSpace(normalized["destination"]))
        {
            var (origin, destination) = useTra
                ? TdxTravelHelper.ExtractTraStations(userQuery)
                : TdxTravelHelper.ExtractThsrStations(userQuery);
            normalized["origin"] ??= origin;
            normalized["destination"] ??= destination;
        }

        if (string.IsNullOrWhiteSpace(normalized["date"]))
        {
            normalized["date"] = TdxTravelHelper.ExtractDate(userQuery)?.ToString("yyyy-MM-dd");
        }

        if (string.IsNullOrWhiteSpace(normalized["time_range"]))
        {
            var timeRange = TdxTravelHelper.ExtractTimeRange(userQuery);
            if (timeRange.HasValue)
            {
                normalized["time_range"] = $"{timeRange.Value.startHour:00}:00-{timeRange.Value.endHour:00}:00";
            }
        }
    }

    private static void MergeFlightQuery(Dictionary<string, string?> normalized, string userQuery)
    {
        if (string.IsNullOrWhiteSpace(normalized["origin"]) || string.IsNullOrWhiteSpace(normalized["destination"]))
        {
            var (origin, destination) = TdxTravelHelper.ExtractAirports(userQuery);
            normalized["origin"] ??= origin;
            normalized["destination"] ??= destination;
        }

        if (string.IsNullOrWhiteSpace(normalized["date"]))
        {
            normalized["date"] = TdxTravelHelper.ExtractDate(userQuery)?.ToString("yyyy-MM-dd");
        }
    }

    private static void MergeBusQuery(Dictionary<string, string?> normalized, string userQuery)
    {
        var (city, routeName) = TdxBusTravelHelper.ExtractCityAndRoute(userQuery);
        normalized["city"] ??= city;
        normalized["route"] ??= routeName;
    }

    private static List<string> GetMissingFields(string mode, Dictionary<string, string?> normalizedQuery)
    {
        var missingFields = new List<string>();

        switch (mode)
        {
            case "rail":
            case "hsr":
            case "flight":
            case "ship":
                if (string.IsNullOrWhiteSpace(normalizedQuery.GetValueOrDefault("origin")))
                    missingFields.Add("origin");
                if (string.IsNullOrWhiteSpace(normalizedQuery.GetValueOrDefault("destination")))
                    missingFields.Add("destination");
                break;
            case "bus":
                if (string.IsNullOrWhiteSpace(normalizedQuery.GetValueOrDefault("city")))
                    missingFields.Add("city");
                if (string.IsNullOrWhiteSpace(normalizedQuery.GetValueOrDefault("route")))
                    missingFields.Add("route");
                break;
        }

        return missingFields;
    }

    private static bool RequiresDate(string mode)
        => mode is "rail" or "hsr" or "flight" or "ship";

    private static async Task<object?> QueryProviderAsync(
        TdxApiService tdxApiService,
        string mode,
        string userQuery,
        ILogger logger,
        CancellationToken ct)
    {
        return mode switch
        {
            "rail" => await TdxTravelHelper.QueryTraTimetableAsync(tdxApiService, userQuery, logger, ct),
            "hsr" => await TdxTravelHelper.QueryThsrTimetableAsync(tdxApiService, userQuery, logger, ct),
            "flight" => await TdxTravelHelper.QueryFlightAsync(tdxApiService, userQuery, logger, ct),
            "bus" => await TdxBusTravelHelper.QueryBusAsync(tdxApiService, userQuery, logger, ct),
            _ => null
        };
    }

    private static string BuildAnswer(
        string mode,
        string userQuery,
        object? rawTdxResult,
        string sourceLabel,
        bool missingDate)
    {
        if (rawTdxResult != null)
        {
            var tdxNode = JsonSerializer.SerializeToElement(rawTdxResult);
            var reply = HighLevelQueryToolMediator.BuildTdxTimetableReply(
                mode,
                userQuery,
                tdxNode,
                sourceLabel,
                DateTimeOffset.UtcNow.ToString("O"));

            if (missingDate)
            {
                return string.Join('\n', new[]
                {
                    "目前先依較寬條件整理可用結果。",
                    "如果你要指定日期或時段，我可以再縮小範圍。",
                    string.Empty,
                    reply
                });
            }

            return reply;
        }

        return missingDate
            ? "目前先依較寬條件整理，但這一輪沒有取得可用結果。如果你要指定日期或時段，我可以再縮小範圍。"
            : "目前沒有取得可用結果。你可以再補充更精確的日期、時段或站點。";
    }

    private static string BuildNeedFollowUpAnswer(string mode, IReadOnlyList<string> missingFields)
    {
        if (mode == "bus" && missingFields.Contains("city"))
        {
            return "我還需要確認所在城市，才能查公車路線與到站資訊。";
        }

        if (missingFields.Contains("origin") || missingFields.Contains("destination"))
        {
            return "我還需要確認起點與終點，才能查詢交通班次。";
        }

        return "我還需要補充幾項資訊，才能繼續查詢。";
    }

    private static TransportFollowUp BuildFollowUp(string mode, IReadOnlyList<string> missingFields)
    {
        if (mode == "bus" && missingFields.Contains("city"))
        {
            return new TransportFollowUp
            {
                Question = "你要查的是哪個城市的公車？",
                FollowUpToken = Guid.NewGuid().ToString("N"),
                Options =
                [
                    new TransportFollowUpOption { Id = "taipei", Label = "台北市" },
                    new TransportFollowUpOption { Id = "new_taipei", Label = "新北市" },
                    new TransportFollowUpOption { Id = "taoyuan", Label = "桃園市" },
                    new TransportFollowUpOption { Id = "other", Label = "其他，我重新描述" }
                ]
            };
        }

        if (missingFields.Contains("origin") || missingFields.Contains("destination"))
        {
            return new TransportFollowUp
            {
                Question = "請再告訴我起點與終點。",
                FollowUpToken = Guid.NewGuid().ToString("N"),
                Options =
                [
                    new TransportFollowUpOption { Id = "restatement", Label = "我重新描述" }
                ]
            };
        }

        return new TransportFollowUp
        {
            Question = "請補充更完整的條件。",
            FollowUpToken = Guid.NewGuid().ToString("N"),
            Options =
            [
                new TransportFollowUpOption { Id = "restatement", Label = "我重新描述" }
            ]
        };
    }

    private static string GetSourceLabel(string mode)
        => mode switch
        {
            "rail" => "TDX 台鐵時刻表 API",
            "hsr" => "TDX 高鐵時刻表 API",
            "bus" => "TDX 公車預估到站 API",
            "flight" => "TDX 航空班表 API (FIDS)",
            _ => "TDX"
        };

    private static string? Get(IDictionary<string, string?> context, string key)
        => context.TryGetValue(key, out var value) ? value : null;

    private static Dictionary<string, object?> ToObjectDictionary(Dictionary<string, string?> source)
        => source.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.OrdinalIgnoreCase);
}
