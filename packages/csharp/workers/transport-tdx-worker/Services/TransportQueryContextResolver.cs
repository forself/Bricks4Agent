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

    public (int startHour, int endHour)? ParseTimeRange(string? timeRange)
        => timeRange switch
        {
            "morning" => (5, 12),
            "afternoon" => (12, 18),
            "evening" => (18, 24),
            "night" => (0, 6),
            _ => null
        };

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

        return map.FirstOrDefault(pair => name.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)).Value;
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

        foreach (var station in stationMap.Keys.OrderByDescending(value => value.Length))
        {
            var index = query.IndexOf(station, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                matches.Add((index, station));
            }
        }

        var ordered = matches
            .OrderBy(item => item.Position)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        return ordered.Count >= 2 ? (ordered[0], ordered[1]) : (null, null);
    }

    private static string? Get(IDictionary<string, string?> source, string key)
        => source.TryGetValue(key, out var value) ? value : null;

    private static DateOnly? ExtractDate(string query)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (query.Contains("明天", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(1);
        }

        if (query.Contains("今天", StringComparison.OrdinalIgnoreCase))
        {
            return today;
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
        if (query.Contains("上午", StringComparison.OrdinalIgnoreCase) || query.Contains("早上", StringComparison.OrdinalIgnoreCase))
        {
            return "morning";
        }

        if (query.Contains("下午", StringComparison.OrdinalIgnoreCase))
        {
            return "afternoon";
        }

        if (query.Contains("晚上", StringComparison.OrdinalIgnoreCase) || query.Contains("夜間", StringComparison.OrdinalIgnoreCase))
        {
            return "evening";
        }

        if (query.Contains("凌晨", StringComparison.OrdinalIgnoreCase))
        {
            return "night";
        }

        return null;
    }
}
