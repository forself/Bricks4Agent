using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class GoogleDriveDelegatedCredentialView
{
    public string Channel { get; set; } = "line";
    public string UserId { get; set; } = string.Empty;
    public string GoogleEmail { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class GoogleDriveOAuthStartResult
{
    public string StateId { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}

public sealed class GoogleDriveOAuthCallbackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string GoogleEmail { get; set; } = string.Empty;
}

public sealed class GoogleDriveOAuthStatus
{
    public bool HasOAuthClientFile { get; set; }
    public string OAuthClientJsonPath { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}

public sealed class GoogleDriveOAuthService
{
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";

    private readonly BrokerDb _db;
    private readonly GoogleDriveDeliveryOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleDriveOAuthService> _logger;

    public GoogleDriveOAuthService(
        BrokerDb db,
        GoogleDriveDeliveryOptions options,
        HttpClient httpClient,
        ILogger<GoogleDriveOAuthService> logger)
    {
        _db = db;
        _options = options;
        _httpClient = httpClient;
        _logger = logger;
    }

    public GoogleDriveOAuthStatus GetStatus()
        => new()
        {
            HasOAuthClientFile = File.Exists(_options.OAuthClientJsonPath),
            OAuthClientJsonPath = _options.OAuthClientJsonPath,
            RedirectUri = _options.DelegatedRedirectUri
        };

    public IReadOnlyList<GoogleDriveDelegatedCredentialView> ListCredentials()
        => _db.Query<GoogleDriveDelegatedCredential>(
            "SELECT * FROM google_drive_delegated_credentials ORDER BY updated_at DESC")
            .Select(item => new GoogleDriveDelegatedCredentialView
            {
                Channel = item.Channel,
                UserId = item.UserId,
                GoogleEmail = item.GoogleEmail,
                Scope = item.Scope,
                Status = item.Status,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            })
            .ToList();

    public GoogleDriveDelegatedCredential? GetCredential(string channel, string userId)
        => _db.Query<GoogleDriveDelegatedCredential>(
            @"SELECT * FROM google_drive_delegated_credentials
              WHERE channel = @channel AND user_id = @userId
              ORDER BY updated_at DESC
              LIMIT 1",
            new { channel, userId }).FirstOrDefault();

    public GoogleDriveOAuthStartResult StartAuthorization(string channel, string userId)
    {
        if (!File.Exists(_options.OAuthClientJsonPath))
            throw new InvalidOperationException("google drive oauth client json not found.");

        var client = LoadOAuthClient(_options.OAuthClientJsonPath);
        var stateId = BrokerCore.IdGen.New("gdo");
        var stateToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var redirectUri = NormalizeRedirectUri(_options.DelegatedRedirectUri);

        var existingStates = _db.Query<GoogleDriveOAuthState>(
            "SELECT * FROM google_drive_oauth_states WHERE channel = @channel AND user_id = @userId AND oauth_state = 'pending'",
            new { channel, userId });
        foreach (var existing in existingStates)
        {
            existing.OAuthState = "superseded";
            _db.Update(existing);
        }

        var state = new GoogleDriveOAuthState
        {
            StateId = stateId,
            Channel = channel,
            UserId = userId,
            RedirectUri = redirectUri,
            StateToken = stateToken,
            OAuthState = "pending",
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
        _db.Insert(state);

        var authUrl =
            $"{client.AuthUri}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(client.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(DriveScope)}" +
            $"&access_type=offline&prompt=consent" +
            $"&state={Uri.EscapeDataString(stateToken)}";

        return new GoogleDriveOAuthStartResult
        {
            StateId = stateId,
            AuthorizationUrl = authUrl,
            RedirectUri = redirectUri
        };
    }

    public async Task<GoogleDriveOAuthCallbackResult> CompleteAuthorizationAsync(
        string stateToken,
        string? code,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var state = _db.Query<GoogleDriveOAuthState>(
            "SELECT * FROM google_drive_oauth_states WHERE state_token = @stateToken ORDER BY created_at DESC LIMIT 1",
            new { stateToken }).FirstOrDefault();

        if (state == null)
            return Fail("invalid_state");

        if (state.ExpiresAt <= DateTime.UtcNow)
        {
            state.OAuthState = "expired";
            _db.Update(state);
            return Fail("state_expired");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            state.OAuthState = "denied";
            _db.Update(state);
            return Fail($"google_oauth_denied: {error}");
        }

        if (string.IsNullOrWhiteSpace(code))
            return Fail("missing_authorization_code");

        var client = LoadOAuthClient(_options.OAuthClientJsonPath);
        var tokenResponse = await ExchangeAuthorizationCodeAsync(client, state.RedirectUri, code, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            return Fail("google_oauth_missing_refresh_token");

        var googleEmail = await ResolveGoogleEmailAsync(tokenResponse.AccessToken, cancellationToken);
        var existing = GetCredential(state.Channel, state.UserId);
        if (existing == null)
        {
            existing = new GoogleDriveDelegatedCredential
            {
                CredentialId = BrokerCore.IdGen.New("gdc"),
                Channel = state.Channel,
                UserId = state.UserId,
                GoogleEmail = googleEmail,
                RefreshToken = tokenResponse.RefreshToken,
                Scope = tokenResponse.Scope,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Insert(existing);
        }
        else
        {
            existing.GoogleEmail = googleEmail;
            existing.RefreshToken = tokenResponse.RefreshToken;
            existing.Scope = tokenResponse.Scope;
            existing.Status = "active";
            existing.UpdatedAt = DateTime.UtcNow;
            _db.Update(existing);
        }

        state.OAuthState = "completed";
        _db.Update(state);

        return new GoogleDriveOAuthCallbackResult
        {
            Success = true,
            Message = "ok",
            Channel = state.Channel,
            UserId = state.UserId,
            GoogleEmail = googleEmail
        };
    }

    public async Task<string> GetDelegatedAccessTokenAsync(string channel, string userId, CancellationToken cancellationToken = default)
    {
        var credential = GetCredential(channel, userId);
        if (credential == null || !string.Equals(credential.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("google_drive_delegated_credential_not_found");

        var client = LoadOAuthClient(_options.OAuthClientJsonPath);
        using var request = new HttpRequestMessage(HttpMethod.Post, client.TokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret,
                ["refresh_token"] = credential.RefreshToken,
                ["grant_type"] = "refresh_token"
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"google_refresh_token_request_failed: {response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("access_token", out var accessTokenNode))
            throw new InvalidOperationException("google_refresh_missing_access_token");

        return accessTokenNode.GetString() ?? throw new InvalidOperationException("google_refresh_empty_access_token");
    }

    private async Task<GoogleOAuthTokenResponse> ExchangeAuthorizationCodeAsync(
        GoogleOAuthClientConfig client,
        string redirectUri,
        string code,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, client.TokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"google_authorization_code_exchange_failed: {response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<GoogleOAuthTokenResponse>(body)
               ?? throw new InvalidOperationException("google_oauth_token_response_invalid");
    }

    private async Task<string> ResolveGoogleEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"google_userinfo_failed: {response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("email", out var emailNode)
            ? emailNode.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeRedirectUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("google_drive_delegated_redirect_uri_missing");

        return value.Trim();
    }

    private static GoogleOAuthClientConfig LoadOAuthClient(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath, Encoding.UTF8);
        var file = JsonSerializer.Deserialize<GoogleOAuthClientFile>(json)
                   ?? throw new InvalidOperationException("google_oauth_client_json_invalid");
        var installed = file.Installed
                        ?? throw new InvalidOperationException("google_oauth_client_installed_section_missing");
        if (string.IsNullOrWhiteSpace(installed.ClientId) ||
            string.IsNullOrWhiteSpace(installed.ClientSecret) ||
            string.IsNullOrWhiteSpace(installed.AuthUri) ||
            string.IsNullOrWhiteSpace(installed.TokenUri))
        {
            throw new InvalidOperationException("google_oauth_client_json_incomplete");
        }

        return new GoogleOAuthClientConfig
        {
            ClientId = installed.ClientId,
            ClientSecret = installed.ClientSecret,
            AuthUri = installed.AuthUri,
            TokenUri = installed.TokenUri
        };
    }

    private static GoogleDriveOAuthCallbackResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}

sealed class GoogleOAuthClientFile
{
    [JsonPropertyName("installed")]
    public GoogleOAuthClientInstalled? Installed { get; set; }
}

sealed class GoogleOAuthClientInstalled
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("auth_uri")]
    public string AuthUri { get; set; } = string.Empty;

    [JsonPropertyName("token_uri")]
    public string TokenUri { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;
}

sealed class GoogleOAuthClientConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthUri { get; set; } = string.Empty;
    public string TokenUri { get; set; } = string.Empty;
}

sealed class GoogleOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}
