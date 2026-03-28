using System.Text.Json;
using Broker.Services;

namespace Broker.Handlers.Travel;

/// <summary>
/// TDX API 整合的交通查詢輔助工具。
/// 解析使用者查詢中的站名，呼叫 TDX API，格式化結構化結果。
/// </summary>
internal static class TdxTravelHelper
{
    // ── 台鐵站名對照表（常用站名 → TDX StationName） ──
    private static readonly Dictionary<string, string> TraStationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["基隆"] = "基隆", ["汐止"] = "汐止", ["南港"] = "南港", ["松山"] = "松山",
        ["台北"] = "臺北", ["臺北"] = "臺北", ["萬華"] = "萬華", ["板橋"] = "板橋",
        ["樹林"] = "樹林", ["桃園"] = "桃園", ["中壢"] = "中壢", ["新竹"] = "新竹",
        ["竹南"] = "竹南", ["苗栗"] = "苗栗", ["豐原"] = "豐原",
        ["台中"] = "臺中", ["臺中"] = "臺中", ["彰化"] = "彰化",
        ["員林"] = "員林", ["田中"] = "田中", ["斗六"] = "斗六",
        ["嘉義"] = "嘉義", ["新營"] = "新營",
        ["台南"] = "臺南", ["臺南"] = "臺南",
        ["高雄"] = "高雄", ["左營"] = "新左營",
        ["新左營"] = "新左營", ["鳳山"] = "鳳山",
        ["屏東"] = "屏東", ["潮州"] = "潮州",
        ["宜蘭"] = "宜蘭", ["羅東"] = "羅東", ["蘇澳"] = "蘇澳新",
        ["花蓮"] = "花蓮", ["台東"] = "臺東", ["臺東"] = "臺東",
        ["瑞芳"] = "瑞芳", ["七堵"] = "七堵", ["鶯歌"] = "鶯歌",
        ["新烏日"] = "新烏日", ["大甲"] = "大甲", ["沙鹿"] = "沙鹿"
    };

    // ── 高鐵站名對照表 ──
    private static readonly Dictionary<string, string> ThsrStationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["南港"] = "南港", ["台北"] = "台北", ["臺北"] = "台北",
        ["板橋"] = "板橋", ["桃園"] = "桃園", ["新竹"] = "新竹",
        ["苗栗"] = "苗栗", ["台中"] = "台中", ["臺中"] = "台中",
        ["彰化"] = "彰化", ["雲林"] = "雲林", ["嘉義"] = "嘉義",
        ["台南"] = "台南", ["臺南"] = "台南",
        ["左營"] = "左營", ["高雄"] = "左營"
    };

    /// <summary>從使用者查詢中提取起訖站</summary>
    public static (string? origin, string? destination) ExtractStations(string query)
    {
        // 常見格式: "台北 台中", "台北到台中", "從台北到台中", "台北→台中"
        var cleaned = query
            .Replace("到", " ")
            .Replace("→", " ")
            .Replace("➜", " ")
            .Replace("->", " ")
            .Replace("至", " ")
            .Replace("從", "")
            .Replace("去", " ")
            .Trim();

        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return (null, null);

        return (parts[0], parts[1]);
    }

    /// <summary>查詢台鐵時刻表</summary>
    public static async Task<object?> QueryTraTimetableAsync(
        TdxApiService tdx, string query, ILogger logger, CancellationToken ct)
    {
        var (origin, destination) = ExtractStations(query);
        if (origin == null || destination == null)
            return null;

        var originStation = TraStationMap.GetValueOrDefault(origin);
        var destStation = TraStationMap.GetValueOrDefault(destination);

        if (originStation == null || destStation == null)
        {
            logger.LogDebug("TDX TRA: station not found for {Origin}→{Dest}", origin, destination);
            return null;
        }

        var doc = await tdx.GetTraDailyTimetableAsync(originStation, destStation, ct);
        if (doc == null)
            return null;

        return FormatTraTimetable(doc, originStation, destStation);
    }

    /// <summary>查詢高鐵時刻表</summary>
    public static async Task<object?> QueryThsrTimetableAsync(
        TdxApiService tdx, string query, ILogger logger, CancellationToken ct)
    {
        var (origin, destination) = ExtractStations(query);
        if (origin == null || destination == null)
            return null;

        var originStation = ThsrStationMap.GetValueOrDefault(origin);
        var destStation = ThsrStationMap.GetValueOrDefault(destination);

        if (originStation == null || destStation == null)
        {
            logger.LogDebug("TDX THSR: station not found for {Origin}→{Dest}", origin, destination);
            return null;
        }

        var doc = await tdx.GetThsrDailyTimetableAsync(originStation, destStation, ct);
        if (doc == null)
            return null;

        return FormatThsrTimetable(doc, originStation, destStation);
    }

    /// <summary>格式化台鐵時刻表結果</summary>
    private static object FormatTraTimetable(JsonDocument doc, string origin, string dest)
    {
        var trains = new List<object>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("DailyTrainInfo", out var trainInfo))
                continue;

            var trainNo = TryGetNestedString(trainInfo, "TrainNo") ?? "";
            var trainTypeName = "";
            if (trainInfo.TryGetProperty("TrainTypeName", out var typeName) &&
                typeName.TryGetProperty("Zh_tw", out var zhName))
            {
                trainTypeName = zhName.GetString() ?? "";
            }

            var departureTime = "";
            var arrivalTime = "";

            if (item.TryGetProperty("OriginStopTime", out var originStop))
                departureTime = TryGetNestedString(originStop, "DepartureTime") ?? "";

            if (item.TryGetProperty("DestinationStopTime", out var destStop))
                arrivalTime = TryGetNestedString(destStop, "ArrivalTime") ?? "";

            if (string.IsNullOrWhiteSpace(departureTime))
                continue;

            trains.Add(new
            {
                train_no = trainNo,
                train_type = trainTypeName,
                departure_time = departureTime,
                arrival_time = arrivalTime
            });
        }

        return new
        {
            source = "TDX 台鐵時刻表 API",
            origin,
            destination = dest,
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            train_count = trains.Count,
            trains = trains.Take(20).ToList()
        };
    }

    /// <summary>格式化高鐵時刻表結果</summary>
    private static object FormatThsrTimetable(JsonDocument doc, string origin, string dest)
    {
        var trains = new List<object>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("DailyTrainInfo", out var trainInfo))
                continue;

            var trainNo = TryGetNestedString(trainInfo, "TrainNo") ?? "";

            var departureTime = "";
            var arrivalTime = "";

            if (item.TryGetProperty("OriginStopTime", out var originStop))
                departureTime = TryGetNestedString(originStop, "DepartureTime") ?? "";

            if (item.TryGetProperty("DestinationStopTime", out var destStop))
                arrivalTime = TryGetNestedString(destStop, "ArrivalTime") ?? "";

            if (string.IsNullOrWhiteSpace(departureTime))
                continue;

            trains.Add(new
            {
                train_no = trainNo,
                departure_time = departureTime,
                arrival_time = arrivalTime
            });
        }

        return new
        {
            source = "TDX 高鐵時刻表 API",
            origin,
            destination = dest,
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            train_count = trains.Count,
            trains = trains.Take(20).ToList()
        };
    }

    private static string? TryGetNestedString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
