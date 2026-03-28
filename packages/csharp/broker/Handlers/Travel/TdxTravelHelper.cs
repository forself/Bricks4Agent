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

    // ── 機場名稱 → IATA Code 對照表 ──
    private static readonly Dictionary<string, string> AirportCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["松山"] = "TSA", ["台北松山"] = "TSA", ["臺北松山"] = "TSA",
        ["桃園"] = "TPE", ["桃園機場"] = "TPE", ["台北桃園"] = "TPE",
        ["高雄"] = "KHH", ["小港"] = "KHH", ["高雄小港"] = "KHH", ["高雄國際"] = "KHH",
        ["台中"] = "RMQ", ["臺中"] = "RMQ", ["清泉崗"] = "RMQ", ["台中清泉崗"] = "RMQ",
        ["台南"] = "TNN", ["臺南"] = "TNN",
        ["嘉義"] = "CYI",
        ["花蓮"] = "HUN",
        ["台東"] = "TTT", ["臺東"] = "TTT",
        ["澎湖"] = "MZG", ["馬公"] = "MZG",
        ["金門"] = "KNH", ["尚義"] = "KNH",
        ["馬祖"] = "LZN", ["南竿"] = "LZN", ["北竿"] = "MFK",
        ["綠島"] = "GNI",
        ["蘭嶼"] = "KYD",
        ["七美"] = "CMJ"
    };

    // 機場代碼 → 顯示名稱
    private static readonly Dictionary<string, string> AirportDisplayName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TSA"] = "松山", ["TPE"] = "桃園", ["KHH"] = "高雄",
        ["RMQ"] = "台中", ["TNN"] = "台南", ["CYI"] = "嘉義",
        ["HUN"] = "花蓮", ["TTT"] = "台東", ["MZG"] = "澎湖",
        ["KNH"] = "金門", ["LZN"] = "南竿", ["MFK"] = "北竿",
        ["GNI"] = "綠島", ["KYD"] = "蘭嶼", ["CMJ"] = "七美"
    };

    /// <summary>
    /// 從自然語言查詢中提取起訖站。
    /// 掃描查詢字串，找出所有匹配已知站名的詞，按出現順序取第一個為起站、第二個為訖站。
    /// 支援：「明天早上板橋往高雄自強號」「從台北到台中 18:00」「南港→左營」等自然語言格式。
    /// </summary>
    public static (string? origin, string? destination) ExtractStations(string query)
    {
        return ExtractStationsFromMap(query, TraStationIdMap) is var tra && tra.origin != null
            ? tra
            : ExtractStationsFromMap(query, ThsrStationIdMap);
    }

    /// <summary>用台鐵站名表提取</summary>
    public static (string? origin, string? destination) ExtractTraStations(string query)
        => ExtractStationsFromMap(query, TraStationIdMap);

    /// <summary>用高鐵站名表提取</summary>
    public static (string? origin, string? destination) ExtractThsrStations(string query)
        => ExtractStationsFromMap(query, ThsrStationIdMap);

    private static (string? origin, string? destination) ExtractStationsFromMap(
        string query, Dictionary<string, string> stationMap)
    {
        // 按站名長度降序排列，優先匹配較長的站名（如「新左營」優先於「左營」）
        var sortedNames = stationMap.Keys.OrderByDescending(k => k.Length).ToList();

        var found = new List<(int position, string name)>();
        var searchText = query;

        foreach (var stationName in sortedNames)
        {
            var idx = searchText.IndexOf(stationName, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            // 檢查是否已經有同位置的更長匹配
            if (found.Any(f => idx >= f.position && idx < f.position + f.name.Length))
                continue;

            found.Add((idx, stationName));
        }

        // 按出現位置排序，第一個是起站，第二個是訖站
        found.Sort((a, b) => a.position.CompareTo(b.position));

        // 需要至少兩個不同的站
        var distinctStations = found
            .Select(f => f.name)
            .Where(name => stationMap.ContainsKey(name))
            .Select(name => (name, id: stationMap[name]))
            .DistinctBy(x => x.id)  // 同一站的不同寫法（台北/臺北）只算一次
            .ToList();

        if (distinctStations.Count < 2)
            return (null, null);

        return (distinctStations[0].name, distinctStations[1].name);
    }

    /// <summary>查詢台鐵時刻表（TDX v2 + 客戶端 OD 過濾）</summary>
    public static async Task<object?> QueryTraTimetableAsync(
        TdxApiService tdx, string query, ILogger logger, CancellationToken ct)
    {
        var (origin, destination) = ExtractTraStations(query);
        logger.LogInformation("TDX TRA: ExtractTraStations(\"{Query}\") → origin=\"{O}\" dest=\"{D}\"",
            query, origin ?? "(null)", destination ?? "(null)");

        if (origin == null || destination == null)
            return null;

        var originId = TraStationIdMap.GetValueOrDefault(origin);
        var destId = TraStationIdMap.GetValueOrDefault(destination);

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
        var (origin, destination) = ExtractThsrStations(query);
        if (origin == null || destination == null)
            return null;

        var originId = ThsrStationIdMap.GetValueOrDefault(origin);
        var destId = ThsrStationIdMap.GetValueOrDefault(destination);

        if (originId == null || destId == null)
            return null;

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

    // ── 航班查詢 ──

    /// <summary>用機場名稱表提取起訖機場</summary>
    public static (string? origin, string? destination) ExtractAirports(string query)
        => ExtractStationsFromMap(query, AirportCodeMap);

    /// <summary>查詢航班（TDX FIDS + 客戶端 OD 過濾）</summary>
    public static async Task<object?> QueryFlightAsync(
        TdxApiService tdx, string query, ILogger logger, CancellationToken ct)
    {
        var (originName, destName) = ExtractAirports(query);
        logger.LogInformation("TDX Flight: ExtractAirports(\"{Query}\") → origin=\"{O}\" dest=\"{D}\"",
            query, originName ?? "(null)", destName ?? "(null)");

        if (originName == null || destName == null)
            return null;

        var originCode = AirportCodeMap.GetValueOrDefault(originName);
        var destCode = AirportCodeMap.GetValueOrDefault(destName);

        if (originCode == null || destCode == null)
            return null;

        var doc = await tdx.GetDomesticFlightsAsync(originCode, ct);
        if (doc == null)
            return null;

        return FilterAndFormatFlights(doc, originCode, destCode, originName, destName, logger);
    }

    private static object? FilterAndFormatFlights(
        JsonDocument doc, string originCode, string destCode,
        string originName, string destName, ILogger logger)
    {
        var flights = new List<object>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var depAirport = item.TryGetProperty("DepartureAirportID", out var depNode) ? depNode.GetString() ?? "" : "";
            var arrAirport = item.TryGetProperty("ArrivalAirportID", out var arrNode) ? arrNode.GetString() ?? "" : "";

            if (!string.Equals(depAirport, originCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(arrAirport, destCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var flightNo = item.TryGetProperty("FlightNumber", out var fnNode) ? fnNode.GetString() ?? "" : "";
            var airlineId = item.TryGetProperty("AirlineID", out var alNode) ? alNode.GetString() ?? "" : "";
            var depTime = item.TryGetProperty("ScheduleDepartureTime", out var depTimeNode) ? depTimeNode.GetString() ?? "" : "";
            var arrTime = item.TryGetProperty("ScheduleArrivalTime", out var arrTimeNode) ? arrTimeNode.GetString() ?? "" : "";
            var depRemark = item.TryGetProperty("DepartureRemark", out var depRemarkNode) ? depRemarkNode.GetString() ?? "" : "";

            // 擷取時間部分 (HH:mm)
            var depTimeShort = depTime.Length >= 16 ? depTime[11..16] : depTime;
            var arrTimeShort = arrTime.Length >= 16 ? arrTime[11..16] : arrTime;

            flights.Add(new
            {
                flight_no = $"{airlineId}{flightNo}",
                airline = airlineId,
                departure_time = depTimeShort,
                arrival_time = arrTimeShort,
                status = depRemark
            });
        }

        logger.LogInformation("TDX Flight: filtered {Count} flights for {O}→{D}", flights.Count, originName, destName);

        if (flights.Count == 0)
            return null;

        var originDisplay = AirportDisplayName.GetValueOrDefault(originCode, originName);
        var destDisplay = AirportDisplayName.GetValueOrDefault(destCode, destName);

        return new
        {
            source = "TDX 航班即時資訊 API (FIDS)",
            origin = $"{originDisplay}({originCode})",
            destination = $"{destDisplay}({destCode})",
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            flight_count = flights.Count,
            flights = flights.Take(20).ToList()
        };
    }
}
