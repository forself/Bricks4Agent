using System.Text.Json;
using Broker.Services;

namespace Broker.Handlers.Travel;

/// <summary>
/// TDX API 整合的交通查詢輔助工具。
/// 解析使用者查詢中的站名，呼叫 TDX API，從全日班次中做客戶端 OD 過濾。
///
/// TDX API 注意事項：
/// - TRA v2 不支援 OD 端點，需用 $filter + 客戶端過濾
/// - THSR v2 DailyTimetable/Today 回傳全日班次，需客戶端過濾起訖站
/// - 站名使用 StationID（非 StationName）做 API 查詢
/// </summary>
public static class TdxTravelHelper
{
    // ── 台鐵站名 → StationID 對照表（常用站） ──
    private static readonly Dictionary<string, string> TraStationIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["基隆"] = "0900", ["七堵"] = "0930", ["汐止"] = "0950", ["南港"] = "0980",
        ["松山"] = "0990", ["台北"] = "1000", ["臺北"] = "1000",
        ["萬華"] = "1010", ["板橋"] = "1020", ["樹林"] = "1030",
        ["鶯歌"] = "1040", ["桃園"] = "1060", ["中壢"] = "1070",
        ["新竹"] = "1210", ["竹南"] = "1250", ["苗栗"] = "1310",
        ["豐原"] = "3280", ["台中"] = "3300", ["臺中"] = "3300",
        ["新烏日"] = "3320", ["彰化"] = "3360", ["員林"] = "3390",
        ["田中"] = "3410", ["斗六"] = "3500", ["嘉義"] = "4050",
        ["新營"] = "4110", ["台南"] = "4220", ["臺南"] = "4220",
        ["新左營"] = "4340", ["左營"] = "4350",
        ["高雄"] = "4400", ["鳳山"] = "4420",
        ["屏東"] = "5000", ["潮州"] = "5050",
        ["宜蘭"] = "7090", ["羅東"] = "7110",
        ["花蓮"] = "7100", ["台東"] = "6000", ["臺東"] = "6000",
        ["瑞芳"] = "0940", ["大甲"] = "2210", ["沙鹿"] = "2240"
    };

    // ── 高鐵站名 → StationID 對照表 ──
    private static readonly Dictionary<string, string> ThsrStationIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["南港"] = "0990", ["台北"] = "1000", ["臺北"] = "1000",
        ["板橋"] = "1010", ["桃園"] = "1020", ["新竹"] = "1030",
        ["苗栗"] = "1035", ["台中"] = "1040", ["臺中"] = "1040",
        ["彰化"] = "1043", ["雲林"] = "1047", ["嘉義"] = "1050",
        ["台南"] = "1060", ["臺南"] = "1060",
        ["左營"] = "1070", ["高雄"] = "1070"
    };

    /// <summary>從使用者查詢中提取起訖站</summary>
    public static (string? origin, string? destination) ExtractStations(string query)
    {
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

    /// <summary>查詢台鐵時刻表（TDX v2 + 客戶端 OD 過濾）</summary>
    public static async Task<object?> QueryTraTimetableAsync(
        TdxApiService tdx, string query, ILogger logger, CancellationToken ct)
    {
        var (origin, destination) = ExtractStations(query);
        logger.LogInformation("TDX TRA: ExtractStations(\"{Query}\") → origin=\"{O}\" dest=\"{D}\"",
            query, origin ?? "(null)", destination ?? "(null)");

        if (origin == null || destination == null)
            return null;

        var originId = TraStationIdMap.GetValueOrDefault(origin);
        var destId = TraStationIdMap.GetValueOrDefault(destination);
        logger.LogInformation("TDX TRA: mapped \"{O}\"→{OID} \"{D}\"→{DID}",
            origin, originId ?? "(null)", destination, destId ?? "(null)");

        if (originId == null || destId == null)
            return null;

        var doc = await tdx.GetTraDailyTimetableByStationAsync(originId, ct);
        if (doc == null)
        {
            logger.LogWarning("TDX TRA API returned null");
            return null;
        }

        return FilterAndFormatTraTimetable(doc, originId, destId, origin, destination, logger);
    }

    /// <summary>查詢高鐵時刻表（TDX v2 全日班次 + 客戶端 OD 過濾）</summary>
    public static async Task<object?> QueryThsrTimetableAsync(
        TdxApiService tdx, string query, ILogger logger, CancellationToken ct)
    {
        var (origin, destination) = ExtractStations(query);
        if (origin == null || destination == null)
            return null;

        var originId = ThsrStationIdMap.GetValueOrDefault(origin);
        var destId = ThsrStationIdMap.GetValueOrDefault(destination);

        if (originId == null || destId == null)
        {
            logger.LogInformation("TDX THSR: station not found for {Origin}→{Dest}", origin, destination);
            return null;
        }

        var doc = await tdx.GetThsrDailyTimetableAsync(ct);
        if (doc == null)
            return null;

        return FilterAndFormatThsrTimetable(doc, originId, destId, origin, destination);
    }

    /// <summary>從全日台鐵班次中過濾出指定起訖站的班次</summary>
    private static object? FilterAndFormatTraTimetable(
        JsonDocument doc, string originId, string destId,
        string originName, string destName, ILogger logger)
    {
        var trains = new List<object>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("StopTimes", out var stopTimes) || stopTimes.ValueKind != JsonValueKind.Array)
                continue;

            int originSeq = -1, destSeq = -1;
            string originDep = "", destArr = "";

            foreach (var stop in stopTimes.EnumerateArray())
            {
                var stationId = stop.TryGetProperty("StationID", out var sidNode) ? sidNode.GetString() ?? "" : "";
                var seq = stop.TryGetProperty("StopSequence", out var seqNode) && seqNode.TryGetInt32(out var s) ? s : -1;

                if (stationId == originId && originSeq < 0)
                {
                    originSeq = seq;
                    originDep = stop.TryGetProperty("DepartureTime", out var depNode) ? depNode.GetString() ?? "" : "";
                }
                else if (stationId == destId && originSeq >= 0) // 訖站必須在起站之後
                {
                    destSeq = seq;
                    destArr = stop.TryGetProperty("ArrivalTime", out var arrNode) ? arrNode.GetString() ?? "" : "";
                    break;
                }
            }

            if (originSeq < 0 || destSeq < 0 || originSeq >= destSeq)
                continue;

            if (string.IsNullOrWhiteSpace(originDep))
                continue;

            var trainInfo = item.TryGetProperty("DailyTrainInfo", out var infoNode) ? infoNode : default;
            var trainNo = trainInfo.ValueKind == JsonValueKind.Object
                ? (trainInfo.TryGetProperty("TrainNo", out var noNode) ? noNode.GetString() ?? "" : "")
                : "";
            var trainTypeName = "";
            if (trainInfo.ValueKind == JsonValueKind.Object &&
                trainInfo.TryGetProperty("TrainTypeName", out var typeNameNode) &&
                typeNameNode.TryGetProperty("Zh_tw", out var zhNode))
            {
                trainTypeName = zhNode.GetString() ?? "";
                // 清理括號內容：「自強(3000)(EMU3000 型電車組)」→「自強」
                var parenIdx = trainTypeName.IndexOf('(');
                if (parenIdx > 0)
                    trainTypeName = trainTypeName[..parenIdx].Trim();
            }

            trains.Add(new
            {
                train_no = trainNo,
                train_type = trainTypeName,
                departure_time = originDep,
                arrival_time = destArr
            });
        }

        logger.LogInformation("TDX TRA: filtered {Count} trains for {O}→{D}", trains.Count, originName, destName);

        if (trains.Count == 0)
            return null;

        return new
        {
            source = "TDX 台鐵時刻表 API",
            origin = originName,
            destination = destName,
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            train_count = trains.Count,
            trains = trains.Take(20).ToList()
        };
    }

    /// <summary>從全日高鐵班次中過濾出指定起訖站的班次</summary>
    private static object? FilterAndFormatThsrTimetable(
        JsonDocument doc, string originId, string destId,
        string originName, string destName)
    {
        var trains = new List<object>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("StopTimes", out var stopTimes) || stopTimes.ValueKind != JsonValueKind.Array)
                continue;

            int originSeq = -1, destSeq = -1;
            string originDep = "", destArr = "";

            foreach (var stop in stopTimes.EnumerateArray())
            {
                var stationId = stop.TryGetProperty("StationID", out var sidNode) ? sidNode.GetString() ?? "" : "";
                var seq = stop.TryGetProperty("StopSequence", out var seqNode) && seqNode.TryGetInt32(out var s) ? s : -1;

                if (stationId == originId && originSeq < 0)
                {
                    originSeq = seq;
                    originDep = stop.TryGetProperty("DepartureTime", out var depNode) ? depNode.GetString() ?? "" : "";
                }
                else if (stationId == destId && originSeq >= 0)
                {
                    destSeq = seq;
                    destArr = stop.TryGetProperty("ArrivalTime", out var arrNode) ? arrNode.GetString() ?? "" : "";
                    break;
                }
            }

            if (originSeq < 0 || destSeq < 0 || originSeq >= destSeq)
                continue;

            if (string.IsNullOrWhiteSpace(originDep))
                continue;

            var trainInfo = item.TryGetProperty("DailyTrainInfo", out var infoNode) ? infoNode : default;
            var trainNo = trainInfo.ValueKind == JsonValueKind.Object
                ? (trainInfo.TryGetProperty("TrainNo", out var noNode) ? noNode.GetString() ?? "" : "")
                : "";

            trains.Add(new
            {
                train_no = trainNo,
                departure_time = originDep,
                arrival_time = destArr
            });
        }

        if (trains.Count == 0)
            return null;

        return new
        {
            source = "TDX 高鐵時刻表 API",
            origin = originName,
            destination = destName,
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            train_count = trains.Count,
            trains = trains.Take(20).ToList()
        };
    }
}
