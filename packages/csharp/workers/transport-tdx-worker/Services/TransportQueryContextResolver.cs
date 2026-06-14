using System.Globalization;
using System.Text.RegularExpressions;

namespace TransportTdxWorker.Services;

public sealed class TransportQueryContextResolver
{
    private static readonly (string Canonical, string[] Aliases, string TdxCity)[] CityAliases =
    [
        ("臺北市", ["臺北市", "台北市", "臺北", "台北"], "Taipei"),
        ("新北市", ["新北市", "新北"], "NewTaipei"),
        ("桃園市", ["桃園市", "桃園"], "Taoyuan"),
        ("臺中市", ["臺中市", "台中市", "臺中", "台中"], "Taichung"),
        ("臺南市", ["臺南市", "台南市", "臺南", "台南"], "Tainan"),
        ("高雄市", ["高雄市", "高雄"], "Kaohsiung")
    ];

    private static readonly Dictionary<string, string> TraStationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["南港"] = "0980",
        ["松山"] = "0990",
        ["臺北"] = "1000",
        ["台北"] = "1000",
        ["萬華"] = "1010",
        ["板橋"] = "1020",
        ["樹林"] = "1040",
        ["桃園"] = "1080",
        ["新竹"] = "1210",
        ["苗栗"] = "3160",
        ["臺中"] = "1310",
        ["台中"] = "1310",
        ["彰化"] = "1120",
        ["嘉義"] = "4080",
        ["臺南"] = "4220",
        ["台南"] = "4220",
        ["新左營"] = "4340",
        ["左營"] = "4340",
        ["高雄"] = "4400"
    };

    private static readonly Dictionary<string, string> ThsrStationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["南港"] = "0990",
        ["臺北"] = "1000",
        ["台北"] = "1000",
        ["板橋"] = "1010",
        ["桃園"] = "1020",
        ["新竹"] = "1030",
        ["苗栗"] = "1035",
        ["臺中"] = "1040",
        ["台中"] = "1040",
        ["彰化"] = "1043",
        ["雲林"] = "1047",
        ["嘉義"] = "1050",
        ["臺南"] = "1060",
        ["台南"] = "1060",
        ["左營"] = "1070",
        ["高雄"] = "1070"
    };

    private static readonly Dictionary<string, string> AirportCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["松山"] = "TSA",
        ["桃園"] = "TPE",
        ["臺北松山"] = "TSA",
        ["台北松山"] = "TSA",
        ["高雄"] = "KHH",
        ["臺中"] = "RMQ",
        ["台中"] = "RMQ",
        ["臺南"] = "TNN",
        ["台南"] = "TNN",
        ["嘉義"] = "CYI",
        ["花蓮"] = "HUN",
        ["臺東"] = "TTT",
        ["台東"] = "TTT",
        ["澎湖"] = "MZG",
        ["金門"] = "KNH",
        ["馬祖"] = "LZN"
    };

    public Dictionary<string, string?> Resolve(string mode, string userQuery, IDictionary<string, string?> context)
    {
        var resolved = new Dictionary<string, string?>(context, StringComparer.OrdinalIgnoreCase);

        if (!resolved.ContainsKey("date"))
        {
            var date = ExtractDate(userQuery);
            if (date.HasValue)
            {
                resolved["date"] = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        if (!resolved.ContainsKey("time_range"))
        {
            var timeRange = ExtractTimeRange(userQuery);
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                resolved["time_range"] = timeRange;
            }
        }

        switch (mode)
        {
            case "rail":
                ApplyStations(resolved, userQuery, TraStationIds);
                break;
            case "hsr":
                ApplyStations(resolved, userQuery, ThsrStationIds);
                break;
            case "flight":
                ApplyStations(resolved, userQuery, AirportCodes);
                break;
            case "bus":
                ApplyBusContext(resolved, userQuery);
                break;
        }

        return resolved;
    }

    public string? NormalizeCityToTdx(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        foreach (var entry in CityAliases)
        {
            if (entry.Aliases.Any(alias => city.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            {
                return entry.TdxCity;
            }
        }

        return null;
    }

    public string? GetTraStationId(string? stationName) => LookupStationId(TraStationIds, stationName);

    public string? GetThsrStationId(string? stationName) => LookupStationId(ThsrStationIds, stationName);

    public string? GetAirportCode(string? airportName) => LookupStationId(AirportCodes, airportName);

    public DateOnly? ParseDate(string? dateText)
        => DateOnly.TryParse(dateText, out var value) ? value : null;

    // Returns an inclusive [startMinute, endMinute] window in minutes-from-midnight, so callers can
    // filter at minute precision (e.g. 18:00-20:00 includes a 20:00 departure).
    public (int startMinute, int endMinute)? ParseTimeRange(string? timeRange)
    {
        if (string.IsNullOrWhiteSpace(timeRange))
        {
            return null;
        }

        switch (timeRange.Trim().ToLowerInvariant())
        {
            case "morning":
                return (5 * 60, 11 * 60 + 59);
            case "afternoon":
                return (12 * 60, 17 * 60 + 59);
            case "evening":
                return (18 * 60, 23 * 60 + 59);
            case "night":
                return (0, 5 * 60 + 59);
        }

        var rangeMatch = Regex.Match(timeRange, @"^(?<sh>\d{1,2}):(?<sm>\d{2})-(?<eh>\d{1,2}):(?<em>\d{2})$");
        if (rangeMatch.Success)
        {
            var start = ToMinutes(rangeMatch.Groups["sh"].Value, rangeMatch.Groups["sm"].Value);
            var end = ToMinutes(rangeMatch.Groups["eh"].Value, rangeMatch.Groups["em"].Value);
            if (start.HasValue && end.HasValue && end.Value >= start.Value)
            {
                return (start.Value, end.Value);
            }

            return null;
        }

        var singleMatch = Regex.Match(timeRange, @"^(?<h>\d{1,2}):(?<m>\d{2})$");
        if (singleMatch.Success)
        {
            var start = ToMinutes(singleMatch.Groups["h"].Value, singleMatch.Groups["m"].Value);
            if (start.HasValue)
            {
                // A single explicit time becomes a focused one-hour departure window.
                return (start.Value, Math.Min(start.Value + 59, 23 * 60 + 59));
            }
        }

        return null;
    }

    private static int? ToMinutes(string hourText, string minuteText)
    {
        if (!int.TryParse(hourText, out var hour) || !int.TryParse(minuteText, out var minute))
        {
            return null;
        }

        if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
        {
            return null;
        }

        return (hour * 60) + minute;
    }

    private static string? LookupStationId(IReadOnlyDictionary<string, string> map, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (map.TryGetValue(name, out var exact))
        {
            return exact;
        }

        // Prefer the longest contained station name so e.g. "新左營站" resolves to 新左營, not 左營.
        return map
            .Where(pair => name.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Key.Length)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }

    private static void ApplyStations(
        IDictionary<string, string?> resolved,
        string userQuery,
        IReadOnlyDictionary<string, string> stationMap)
    {
        if (!string.IsNullOrWhiteSpace(Get(resolved, "origin")) &&
            !string.IsNullOrWhiteSpace(Get(resolved, "destination")))
        {
            return;
        }

        var (origin, destination) = ExtractOrderedStations(userQuery, stationMap);
        if (string.IsNullOrWhiteSpace(Get(resolved, "origin")))
        {
            resolved["origin"] = origin;
        }

        if (string.IsNullOrWhiteSpace(Get(resolved, "destination")))
        {
            resolved["destination"] = destination;
        }
    }

    private void ApplyBusContext(IDictionary<string, string?> resolved, string userQuery)
    {
        if (string.IsNullOrWhiteSpace(Get(resolved, "city")))
        {
            foreach (var entry in CityAliases)
            {
                if (entry.Aliases.Any(alias => userQuery.Contains(alias, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved["city"] = entry.Canonical;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(Get(resolved, "route")))
        {
            return;
        }

        var routeMatch = Regex.Match(userQuery, @"(?<route>\d{1,4}[A-Za-z]?|紅\d+|藍\d+|綠\d+|橘\d+|棕\d+)");
        if (routeMatch.Success)
        {
            resolved["route"] = routeMatch.Groups["route"].Value;
        }
    }

    private static (string? origin, string? destination) ExtractOrderedStations(
        string query,
        IReadOnlyDictionary<string, string> stationMap)
    {
        var matches = new List<(int Position, string Name)>();
        var consumed = new bool[query.Length];

        // Longest-first with span masking: once "新左營" claims its characters, the substring
        // "左營" inside it cannot also match, so overlapping station names never double-count.
        foreach (var station in stationMap.Keys
                     .OrderByDescending(value => value.Length)
                     .ThenBy(value => value, StringComparer.Ordinal))
        {
            var searchFrom = 0;
            while (searchFrom <= query.Length - station.Length)
            {
                var index = query.IndexOf(station, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                var overlaps = false;
                for (var i = index; i < index + station.Length; i++)
                {
                    if (consumed[i])
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    matches.Add((index, station));
                    for (var i = index; i < index + station.Length; i++)
                    {
                        consumed[i] = true;
                    }

                    break;
                }

                searchFrom = index + 1;
            }
        }

        var ordered = matches
            .OrderBy(item => item.Position)
            .Select(item => item.Name)
            .Take(2)
            .ToList();

        return ordered.Count >= 2 ? (ordered[0], ordered[1]) : (null, null);
    }

    private static string? Get(IDictionary<string, string?> source, string key)
        => source.TryGetValue(key, out var value) ? value : null;

    private static DayOfWeek? MapWeekday(string day) => day switch
    {
        "一" => DayOfWeek.Monday,
        "二" => DayOfWeek.Tuesday,
        "三" => DayOfWeek.Wednesday,
        "四" => DayOfWeek.Thursday,
        "五" => DayOfWeek.Friday,
        "六" => DayOfWeek.Saturday,
        "日" or "天" => DayOfWeek.Sunday,
        _ => null
    };

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek target, bool includeToday)
    {
        var diff = (((int)target - (int)from.DayOfWeek) + 7) % 7;
        if (diff == 0 && !includeToday)
        {
            diff = 7;
        }

        return from.AddDays(diff);
    }

    private static DateOnly NextWeekInIso(DateOnly from, DayOfWeek target)
    {
        // ISO week starts Monday. "下週X" means weekday X within the next calendar week.
        var isoToday = from.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)from.DayOfWeek;
        var nextMonday = from.AddDays(8 - isoToday);
        var isoTarget = target == DayOfWeek.Sunday ? 7 : (int)target;
        return nextMonday.AddDays(isoTarget - 1);
    }

    private static DateOnly? ExtractDate(string query)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (query.Contains("大後天", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(3);
        }

        if (query.Contains("後天", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(2);
        }

        if (query.Contains("明天", StringComparison.OrdinalIgnoreCase) || query.Contains("明日", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(1);
        }

        if (query.Contains("今天", StringComparison.OrdinalIgnoreCase) || query.Contains("今日", StringComparison.OrdinalIgnoreCase))
        {
            return today;
        }

        // Weekend: 週末 / 這週末 = upcoming Saturday; 下週末 = next week's Saturday.
        var weekend = Regex.Match(query, @"(?<next>下)?\s*(?:這|本)?\s*(?:週末|周末)");
        if (weekend.Success)
        {
            var saturday = NextWeekday(today, DayOfWeek.Saturday, includeToday: true);
            return weekend.Groups["next"].Success ? saturday.AddDays(7) : saturday;
        }

        // Weekday: 週一/星期一/禮拜一 = next occurrence; 下週一 = same weekday in next ISO week.
        var weekday = Regex.Match(query, @"(?<next>下)?\s*(?:這|本)?\s*(?:週|周|星期|禮拜)(?<day>[一二三四五六日天])");
        if (weekday.Success)
        {
            var dow = MapWeekday(weekday.Groups["day"].Value);
            if (dow.HasValue)
            {
                return weekday.Groups["next"].Success
                    ? NextWeekInIso(today, dow.Value)
                    : NextWeekday(today, dow.Value, includeToday: false);
            }
        }

        var isoMatch = Regex.Match(query, @"(?<date>\d{4}-\d{1,2}-\d{1,2})");
        if (isoMatch.Success && DateOnly.TryParse(isoMatch.Groups["date"].Value, out var isoDate))
        {
            return isoDate;
        }

        var mdMatch = Regex.Match(query, @"(?<month>\d{1,2})/(?<day>\d{1,2})");
        if (mdMatch.Success)
        {
            var month = int.Parse(mdMatch.Groups["month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(mdMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
            var year = today.Year;
            if (month < today.Month || (month == today.Month && day < today.Day))
            {
                year++;
            }

            return new DateOnly(year, month, day);
        }

        return null;
    }

    private static string? ExtractTimeRange(string query)
    {
        // Absolute range first: 18:00-20:00 / 18:00~20:00 / 18點到20點 / 下午6點-8點.
        var range = Regex.Match(
            query,
            @"(?<smer>上午|早上|凌晨|中午|下午|晚上|傍晚|夜間)?\s*(?<sh>\d{1,2})\s*[:：點時](?<sm>\d{0,2})\s*[-~～－—至到]\s*(?<emer>上午|早上|凌晨|中午|下午|晚上|傍晚|夜間)?\s*(?<eh>\d{1,2})\s*[:：點時](?<em>\d{0,2})");
        if (range.Success)
        {
            var startMer = range.Groups["smer"].Success ? range.Groups["smer"].Value : InferMeridiem(query);
            var endMer = range.Groups["emer"].Success ? range.Groups["emer"].Value : startMer;
            var start = FormatClock(range.Groups["sh"].Value, range.Groups["sm"].Value, startMer);
            var end = FormatClock(range.Groups["eh"].Value, range.Groups["em"].Value, endMer);
            if (start is not null && end is not null)
            {
                return $"{start}-{end}";
            }
        }

        // Single absolute time, optionally with a meridiem: 下午3點 / 晚上7:30 / 18:00.
        var single = Regex.Match(
            query,
            @"(?<mer>上午|早上|凌晨|中午|下午|晚上|傍晚|夜間)?\s*(?<h>\d{1,2})\s*[:：點時](?<m>\d{0,2})");
        if (single.Success)
        {
            var mer = single.Groups["mer"].Success ? single.Groups["mer"].Value : InferMeridiem(query);
            var clock = FormatClock(single.Groups["h"].Value, single.Groups["m"].Value, mer);
            if (clock is not null)
            {
                return clock;
            }
        }

        if (query.Contains("上午", StringComparison.OrdinalIgnoreCase) || query.Contains("早上", StringComparison.OrdinalIgnoreCase))
        {
            return "morning";
        }

        if (query.Contains("中午", StringComparison.OrdinalIgnoreCase) || query.Contains("下午", StringComparison.OrdinalIgnoreCase))
        {
            return "afternoon";
        }

        if (query.Contains("晚上", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("夜間", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("傍晚", StringComparison.OrdinalIgnoreCase))
        {
            return "evening";
        }

        if (query.Contains("凌晨", StringComparison.OrdinalIgnoreCase))
        {
            return "night";
        }

        return null;
    }

    private static string InferMeridiem(string query)
    {
        if (query.Contains("下午", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("晚上", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("傍晚", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("夜間", StringComparison.OrdinalIgnoreCase))
        {
            return "下午";
        }

        if (query.Contains("凌晨", StringComparison.OrdinalIgnoreCase))
        {
            return "凌晨";
        }

        return string.Empty;
    }

    private static string? FormatClock(string hourText, string minuteText, string meridiem)
    {
        if (!int.TryParse(hourText, out var hour))
        {
            return null;
        }

        var minute = string.IsNullOrEmpty(minuteText) ? 0 : (int.TryParse(minuteText, out var m) ? m : -1);
        if (minute < 0 || minute > 59 || hour < 0 || hour > 23)
        {
            return null;
        }

        switch (meridiem)
        {
            case "下午":
            case "晚上":
            case "傍晚":
            case "夜間":
                if (hour >= 1 && hour <= 11)
                {
                    hour += 12;
                }

                break;
            case "凌晨":
                if (hour == 12)
                {
                    hour = 0;
                }

                break;
        }

        return $"{hour:00}:{minute:00}";
    }
}
