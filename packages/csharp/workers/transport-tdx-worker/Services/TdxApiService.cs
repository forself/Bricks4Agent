using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TransportTdxWorker.Services;

public sealed class TdxApiService
{
    private readonly TdxOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TdxApiService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public TdxApiService(TdxOptions options, HttpClient httpClient, ILogger<TdxApiService> logger)
    {
        _options = options;
        _httpClient = httpClient;
        _logger = logger;
    }

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

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            });

            using var response = await _httpClient.PostAsync(_options.AuthUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TDX token request failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<JsonDocument?> GetAsync(string path, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{_options.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("TDX request failed: {Path} {Status}", path, response.StatusCode);
            return null;
        }

        return JsonDocument.Parse(body);
    }

    public Task<JsonDocument?> GetTraDailyTimetableByStationAsync(string originStationId, DateOnly? date = null, CancellationToken ct = default)
    {
        var datePart = date.HasValue ? $"TrainDate/{date.Value:yyyy-MM-dd}" : "Today";
        var filter = Uri.EscapeDataString($"StopTimes/any(s:s/StationID eq '{originStationId}')");
        return GetAsync($"v2/Rail/TRA/DailyTimetable/{datePart}?$filter={filter}&$format=JSON", ct);
    }

    public Task<JsonDocument?> GetThsrDailyTimetableAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var datePart = date.HasValue ? $"TrainDate/{date.Value:yyyy-MM-dd}" : "Today";
        return GetAsync($"v2/Rail/THSR/DailyTimetable/{datePart}?$format=JSON", ct);
    }

    public Task<JsonDocument?> GetCityBusEstimatedTimeAsync(string city, string routeName, CancellationToken ct = default)
        => GetAsync($"v2/Bus/EstimatedTimeOfArrival/City/{Uri.EscapeDataString(city)}/{Uri.EscapeDataString(routeName)}?$format=JSON", ct);

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
