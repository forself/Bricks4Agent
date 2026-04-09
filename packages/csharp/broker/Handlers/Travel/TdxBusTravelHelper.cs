using System.Text.Json;
using System.Text.RegularExpressions;
using Broker.Services;

namespace Broker.Handlers.Travel;

internal static class TdxBusTravelHelper
{
    private sealed record BusEtaEntry(
        string RouteName,
        string StopName,
        int? EstimateMinutes,
        int? StopStatus,
        string Direction);

    private static readonly IReadOnlyList<(string Canonical, string[] Aliases)> CityAliases =
    [
        ("臺北市", ["臺北市", "台北市", "台北", "臺北"]),
        ("新北市", ["新北市", "新北"]),
        ("桃園市", ["桃園市", "桃園"]),
        ("臺中市", ["臺中市", "台中市", "台中", "臺中"]),
        ("臺南市", ["臺南市", "台南市", "台南", "臺南"]),
        ("高雄市", ["高雄市", "高雄"]),
        ("基隆市", ["基隆市", "基隆"]),
        ("新竹市", ["新竹市", "新竹市區"]),
        ("新竹縣", ["新竹縣"]),
        ("苗栗縣", ["苗栗縣", "苗栗"]),
        ("彰化縣", ["彰化縣", "彰化"]),
        ("南投縣", ["南投縣", "南投"]),
        ("雲林縣", ["雲林縣", "雲林"]),
        ("嘉義市", ["嘉義市"]),
        ("嘉義縣", ["嘉義縣"]),
        ("屏東縣", ["屏東縣", "屏東"]),
        ("宜蘭縣", ["宜蘭縣", "宜蘭"]),
        ("花蓮縣", ["花蓮縣", "花蓮"]),
        ("臺東縣", ["臺東縣", "台東縣", "台東", "臺東"]),
        ("澎湖縣", ["澎湖縣", "澎湖"]),
        ("金門縣", ["金門縣", "金門"]),
        ("連江縣", ["連江縣", "馬祖", "連江"])
    ];

    private static readonly Regex RouteTokenRegex = new(
        @"(?<route>[0-9A-Za-z]+(?:[A-Za-z0-9-]+)?|[紅綠藍棕橘黃紫幹副區快內外甲乙丙丁南北東西環支]+(?:線|路)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<object?> QueryBusAsync(
        TdxApiService tdx,
        string query,
        ILogger logger,
        CancellationToken ct)
    {
        var (city, routeName) = ExtractCityAndRoute(query);
        logger.LogInformation(
            "TDX Bus: ExtractCityAndRoute(\"{Query}\") => city=\"{City}\" route=\"{Route}\"",
            query,
            city ?? "(null)",
            routeName ?? "(null)");

        if (city == null || routeName == null)
            return null;

        var doc = await tdx.GetCityBusEstimatedTimeAsync(city, routeName, ct);
        if (doc == null)
            return null;

        return FilterAndFormatBusEstimatedTime(doc, city, routeName, logger);
    }

    public static (string? city, string? routeName) ExtractCityAndRoute(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (null, null);

        var normalized = Regex.Replace(query, @"\s+", " ").Trim();
        foreach (var (canonical, aliases) in CityAliases)
        {
            foreach (var alias in aliases.OrderByDescending(text => text.Length))
            {
                var index = normalized.IndexOf(alias, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;

                var remainder = (normalized[..index] + " " + normalized[(index + alias.Length)..]).Trim();
                var routeName = ExtractRouteName(remainder);
                if (!string.IsNullOrWhiteSpace(routeName))
                    return (canonical, routeName);
            }
        }

        return (null, ExtractRouteName(normalized));
    }

    private static string? ExtractRouteName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = Regex.Replace(text, @"[，,。．、!?？！]", " ");
        cleaned = Regex.Replace(
            cleaned,
            @"\b(公車|巴士|到站|預估|站牌|路線|查詢|多久|何時|現在|今天|明天|班次|資訊|時間)\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        var match = RouteTokenRegex.Match(cleaned);
        return match.Success ? match.Groups["route"].Value.Trim() : cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static object FilterAndFormatBusEstimatedTime(
        JsonDocument doc,
        string city,
        string routeName,
        ILogger logger)
    {
        var buses = new List<BusEtaEntry>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var resolvedRouteName = GetLocalizedText(item, "RouteName");
            if (!string.IsNullOrWhiteSpace(resolvedRouteName) &&
                !string.Equals(resolvedRouteName, routeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stopName = GetLocalizedText(item, "StopName");
            if (string.IsNullOrWhiteSpace(stopName))
                continue;

            int? estimateSeconds = item.TryGetProperty("EstimateTime", out var estimateNode) && estimateNode.ValueKind == JsonValueKind.Number && estimateNode.TryGetInt32(out var seconds)
                ? seconds
                : null;
            int? estimateMinutes = estimateSeconds.HasValue
                ? Math.Max(0, (int)Math.Ceiling(estimateSeconds.Value / 60d))
                : null;
            int? stopStatus = item.TryGetProperty("StopStatus", out var stopStatusNode) && stopStatusNode.ValueKind == JsonValueKind.Number && stopStatusNode.TryGetInt32(out var status)
                ? status
                : null;
            var direction = item.TryGetProperty("Direction", out var directionNode) && directionNode.TryGetInt32(out var directionValue)
                ? FormatDirection(directionValue)
                : string.Empty;

            buses.Add(new BusEtaEntry(
                string.IsNullOrWhiteSpace(resolvedRouteName) ? routeName : resolvedRouteName,
                stopName,
                estimateMinutes,
                stopStatus,
                direction));
        }

        var orderedBuses = buses
            .OrderBy(entry => entry.EstimateMinutes ?? int.MaxValue)
            .ThenBy(entry => entry.StopStatus ?? int.MaxValue)
            .Take(20)
            .Select(entry => new
            {
                route_name = entry.RouteName,
                stop_name = entry.StopName,
                estimate_minutes = entry.EstimateMinutes,
                stop_status = entry.StopStatus,
                direction = entry.Direction
            })
            .ToList();

        logger.LogInformation(
            "TDX Bus: filtered {Count} ETA records for {City} {Route}",
            orderedBuses.Count,
            city,
            routeName);

        return new
        {
            source = "TDX 公車預估到站 API",
            origin = city,
            destination = routeName,
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            bus_count = orderedBuses.Count,
            buses = orderedBuses
        };
    }

    private static string GetLocalizedText(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var node))
            return string.Empty;

        if (node.ValueKind == JsonValueKind.String)
            return node.GetString() ?? string.Empty;

        if (node.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (node.TryGetProperty("Zh_tw", out var zhNode))
            return zhNode.GetString() ?? string.Empty;

        return node.EnumerateObject().Select(property => property.Value.GetString()).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
    }

    private static string FormatDirection(int direction)
        => direction switch
        {
            0 => "去程",
            1 => "返程",
            2 => "迴圈",
            _ => string.Empty
        };
}
