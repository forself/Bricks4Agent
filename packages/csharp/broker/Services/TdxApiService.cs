using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Broker.Services;

/// <summary>
/// TDX 運輸資料流通服務平臺 API 客戶端
///
/// 處理 OAuth2 Client Credentials 認證與 API 呼叫。
/// Access Token 有效期 1 天，自動刷新。
/// 速率限制：每 IP 每秒 50 次請求。
///
/// API 文件：https://tdx.transportdata.tw
/// </summary>
public sealed class TdxApiService
{
    private readonly TdxOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TdxApiService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public bool IsConfigured =>
        HasRealCredential(_options.ClientId) &&
        HasRealCredential(_options.ClientSecret);

    private static bool HasRealCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return !value.StartsWith("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
    }

    public TdxApiService(TdxOptions options, HttpClient httpClient, ILogger<TdxApiService> logger)
    {
        _options = options;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>取得有效的 Access Token（自動刷新）</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-5))
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-5))
                return _accessToken;

            _logger.LogInformation("Requesting TDX access token...");

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            });

            using var response = await _httpClient.PostAsync(_options.AuthUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("TDX token request failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            _logger.LogInformation("TDX access token obtained, expires in {Seconds}s", expiresIn);
            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TDX token request exception");
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>呼叫 TDX API（自動帶 Bearer token）</summary>
    public async Task<JsonDocument?> GetAsync(string path, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        if (token == null)
        {
            _logger.LogWarning("TDX API call skipped: no access token");
            return null;
        }

        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{_options.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("TDX API {Path} returned {Status}: {Body}",
                path, response.StatusCode, body.Length > 200 ? body[..200] : body);
            return null;
        }

        return JsonDocument.Parse(body);
    }

    // ── 台鐵 API ──

    /// <summary>台鐵指定日期時刻表（經過指定起站的所有班次，由客戶端做 OD 過濾）</summary>
    public Task<JsonDocument?> GetTraDailyTimetableByStationAsync(
        string originStationId, DateOnly? date = null, CancellationToken ct = default)
    {
        var datePart = date.HasValue ? $"TrainDate/{date.Value:yyyy-MM-dd}" : "Today";
        var filter = Uri.EscapeDataString($"StopTimes/any(s:s/StationID eq '{originStationId}')");
        var path = $"v2/Rail/TRA/DailyTimetable/{datePart}?$filter={filter}&$format=JSON";
        return GetAsync(path, ct);
    }

    /// <summary>台鐵車站列表</summary>
    public Task<JsonDocument?> GetTraStationsAsync(CancellationToken ct = default)
        => GetAsync("v2/Rail/TRA/Station?$format=JSON", ct);

    /// <summary>台鐵即時到離站</summary>
    public Task<JsonDocument?> GetTraLiveBoardAsync(string stationId, CancellationToken ct = default)
        => GetAsync($"v2/Rail/TRA/LiveBoard/Station/{Uri.EscapeDataString(stationId)}?$top=20&$format=JSON", ct);

    // ── 高鐵 API ──

    /// <summary>高鐵指定日期全部班次時刻表</summary>
    public Task<JsonDocument?> GetThsrDailyTimetableAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var datePart = date.HasValue ? $"TrainDate/{date.Value:yyyy-MM-dd}" : "Today";
        return GetAsync($"v2/Rail/THSR/DailyTimetable/{datePart}?$format=JSON", ct);
    }

    /// <summary>高鐵車站列表</summary>
    public Task<JsonDocument?> GetThsrStationsAsync(CancellationToken ct = default)
        => GetAsync("v2/Rail/THSR/Station?$format=JSON", ct);

    // ── 公車 API ──

    /// <summary>市區公車路線（指定城市）</summary>
    public Task<JsonDocument?> GetCityBusRoutesAsync(string city, CancellationToken ct = default)
        => GetAsync($"v2/Bus/Route/City/{Uri.EscapeDataString(city)}?$format=JSON", ct);

    /// <summary>市區公車預估到站時間（指定城市+路線）</summary>
    public Task<JsonDocument?> GetCityBusEstimatedTimeAsync(
        string city, string routeName, CancellationToken ct = default)
    {
        var path = $"v2/Bus/EstimatedTimeOfArrival/City/{Uri.EscapeDataString(city)}/{Uri.EscapeDataString(routeName)}?$format=JSON";
        return GetAsync(path, ct);
    }

    /// <summary>公路客運路線</summary>
    public Task<JsonDocument?> GetInterCityBusRoutesAsync(CancellationToken ct = default)
        => GetAsync("v2/Bus/Route/InterCity?$format=JSON", ct);

    // ── 航空 API ──

    /// <summary>國內航班即時資訊（可選依起飛機場過濾）</summary>
    public Task<JsonDocument?> GetDomesticFlightsAsync(string? departureAirportId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(departureAirportId))
        {
            var filter = Uri.EscapeDataString($"DepartureAirportID eq '{departureAirportId}'");
            return GetAsync($"v2/Air/FIDS/Flight?$filter={filter}&$orderby=ScheduleDepartureTime&$format=JSON", ct);
        }
        return GetAsync("v2/Air/FIDS/Flight?$orderby=ScheduleDepartureTime&$format=JSON", ct);
    }
}

public sealed class TdxOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthUrl { get; set; } = "https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token";
    public string BaseUrl { get; set; } = "https://tdx.transportdata.tw/api/basic";
}
