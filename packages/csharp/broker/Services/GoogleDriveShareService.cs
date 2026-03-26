using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerCore.Contracts;

namespace Broker.Services;

public sealed class GoogleDriveDeliveryOptions
{
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string OAuthClientJsonPath { get; set; } = string.Empty;
    public string DefaultFolderId { get; set; } = string.Empty;
    public string DefaultShareMode { get; set; } = "anyone_with_link";
    public string DefaultPermissionRole { get; set; } = "reader";
    public string DelegatedRedirectUri { get; set; } = "http://127.0.0.1:5361/api/v1/google-drive/oauth/callback";
}

public sealed class GoogleDriveShareRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string ShareMode { get; set; } = string.Empty;
    public string IdentityMode { get; set; } = "system_account";
    public string Channel { get; set; } = "line";
    public string UserId { get; set; } = string.Empty;
}

public sealed class GoogleDriveShareStatus
{
    public bool Enabled { get; set; }
    public bool HasCredentialFile { get; set; }
    public bool HasOAuthClientFile { get; set; }
    public bool HasDefaultFolderId { get; set; }
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string OAuthClientJsonPath { get; set; } = string.Empty;
    public string DefaultFolderId { get; set; } = string.Empty;
}

public sealed class GoogleDriveShareService
{
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";

    private readonly GoogleDriveDeliveryOptions _options;
    private readonly GoogleDriveOAuthService _oauthService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleDriveShareService> _logger;

    public GoogleDriveShareService(
        GoogleDriveDeliveryOptions options,
        GoogleDriveOAuthService oauthService,
        HttpClient httpClient,
        ILogger<GoogleDriveShareService> logger)
    {
        _options = options;
        _oauthService = oauthService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public GoogleDriveShareStatus GetStatus()
        => new()
        {
            Enabled = (File.Exists(_options.ServiceAccountJsonPath) || File.Exists(_options.OAuthClientJsonPath))
                      && !string.IsNullOrWhiteSpace(_options.DefaultFolderId),
            HasCredentialFile = File.Exists(_options.ServiceAccountJsonPath),
            HasOAuthClientFile = File.Exists(_options.OAuthClientJsonPath),
            HasDefaultFolderId = !string.IsNullOrWhiteSpace(_options.DefaultFolderId),
            ServiceAccountJsonPath = _options.ServiceAccountJsonPath,
            OAuthClientJsonPath = _options.OAuthClientJsonPath,
            DefaultFolderId = _options.DefaultFolderId
        };

    public async Task<GoogleDriveShareResult> ShareFileAsync(
        GoogleDriveShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath) || !Path.IsPathRooted(request.FilePath))
            return Fail("file_path must be an absolute path.");

        if (!File.Exists(request.FilePath))
            return Fail("file_path not found.");

        var folderId = string.IsNullOrWhiteSpace(request.FolderId) ? _options.DefaultFolderId : request.FolderId.Trim();
        if (string.IsNullOrWhiteSpace(folderId))
            return Fail("google drive folder id is required.");

        var shareMode = string.IsNullOrWhiteSpace(request.ShareMode) ? _options.DefaultShareMode : request.ShareMode.Trim();
        if (!string.Equals(shareMode, "anyone_with_link", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(shareMode, "restricted", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("share_mode must be anyone_with_link or restricted.");
        }

        var fileName = string.IsNullOrWhiteSpace(request.FileName)
            ? Path.GetFileName(request.FilePath)
            : request.FileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            return Fail("resolved file name is empty.");

        var identityMode = string.IsNullOrWhiteSpace(request.IdentityMode) ? "system_account" : request.IdentityMode.Trim().ToLowerInvariant();
        string accessToken;
        GoogleServiceAccountCredential? credential = null;
        if (identityMode == "user_delegated")
        {
            if (string.IsNullOrWhiteSpace(request.Channel) || string.IsNullOrWhiteSpace(request.UserId))
                return Fail("channel and user_id are required for user_delegated delivery.");
            accessToken = await _oauthService.GetDelegatedAccessTokenAsync(request.Channel, request.UserId, cancellationToken);
        }
        else
        {
            if (!File.Exists(_options.ServiceAccountJsonPath))
                return Fail("google drive service account json not found.");
            credential = LoadCredential(_options.ServiceAccountJsonPath);
            accessToken = await GetAccessTokenAsync(credential, cancellationToken);
        }

        var uploadResult = await UploadFileAsync(accessToken, request.FilePath, fileName, folderId, cancellationToken);
        if (!uploadResult.Success)
            return uploadResult;

        if (string.Equals(shareMode, "anyone_with_link", StringComparison.OrdinalIgnoreCase))
        {
            var permissionError = await CreateAnyonePermissionAsync(accessToken, uploadResult.FileId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(permissionError))
                return Fail(permissionError);
        }

        uploadResult.ShareMode = shareMode;
        uploadResult.SourcePath = request.FilePath;
        uploadResult.Message = "ok";
        return uploadResult;
    }

    private async Task<string> GetAccessTokenAsync(
        GoogleServiceAccountCredential credential,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var assertion = BuildJwtAssertion(credential, now);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, credential.TokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            })
        };

        using var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"google_token_request_failed: {tokenResponse.StatusCode}: {tokenBody}");

        using var tokenDoc = JsonDocument.Parse(tokenBody);
        if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenNode))
            throw new InvalidOperationException("google_token_missing_access_token");

        return accessTokenNode.GetString() ?? throw new InvalidOperationException("google_token_empty_access_token");
    }

    private async Task<GoogleDriveShareResult> UploadFileAsync(
        string accessToken,
        string filePath,
        string fileName,
        string folderId,
        CancellationToken cancellationToken)
    {
        var mimeType = GetMimeType(filePath);
        var metadataJson = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { folderId }
        });
        var boundary = "b4a_" + Guid.NewGuid().ToString("N");
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        using var content = new MultipartContent("related", boundary);
        var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metadataContent);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(fileContent);

        var uploadUri = "https://www.googleapis.com/upload/drive/v3/files" +
                        "?uploadType=multipart&supportsAllDrives=true&fields=id,name,mimeType,webViewLink,webContentLink,resourceKey";
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (body.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    $"google_drive_upload_failed: {response.StatusCode}: service account uploads require a Shared Drive folder or OAuth-delegated user Drive. {body}");
            }

            return Fail($"google_drive_upload_failed: {response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var fileId = doc.RootElement.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
        var resourceKey = doc.RootElement.TryGetProperty("resourceKey", out var resourceKeyNode) ? resourceKeyNode.GetString() ?? string.Empty : string.Empty;
        var webViewLink = doc.RootElement.TryGetProperty("webViewLink", out var webViewNode)
            ? webViewNode.GetString() ?? string.Empty
            : string.Empty;
        var webContentLink = doc.RootElement.TryGetProperty("webContentLink", out var webContentNode)
            ? webContentNode.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(fileId))
            return Fail("google_drive_upload_missing_file_id");

        if (string.IsNullOrWhiteSpace(webViewLink))
            webViewLink = BuildWebViewLink(fileId, resourceKey);
        if (string.IsNullOrWhiteSpace(webContentLink))
            webContentLink = BuildDownloadLink(fileId, resourceKey);

        return new GoogleDriveShareResult
        {
            Success = true,
            FileId = fileId,
            FileName = fileName,
            FolderId = folderId,
            MimeType = mimeType,
            WebViewLink = webViewLink,
            DownloadLink = webContentLink,
            ResourceKey = resourceKey
        };
    }

    private async Task<string?> CreateAnyonePermissionAsync(
        string accessToken,
        string fileId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://www.googleapis.com/drive/v3/files/{fileId}/permissions?supportsAllDrives=true&fields=id");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                role = _options.DefaultPermissionRole,
                type = "anyone",
                allowFileDiscovery = false
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return $"google_drive_permission_failed: {response.StatusCode}: {body}";
    }

    private static string BuildJwtAssertion(GoogleServiceAccountCredential credential, DateTimeOffset now)
    {
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            typ = "JWT"
        }));

        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = credential.ClientEmail,
            scope = DriveScope,
            aud = credential.TokenUri,
            exp = now.AddMinutes(55).ToUnixTimeSeconds(),
            iat = now.ToUnixTimeSeconds()
        }));

        var unsigned = $"{header}.{payload}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credential.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Base64UrlEncode(signature)}";
    }

    private static GoogleServiceAccountCredential LoadCredential(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath, Encoding.UTF8);
        var credential = JsonSerializer.Deserialize<GoogleServiceAccountCredential>(json)
            ?? throw new InvalidOperationException("google_service_account_json_invalid");
        if (string.IsNullOrWhiteSpace(credential.ClientEmail) ||
            string.IsNullOrWhiteSpace(credential.PrivateKey) ||
            string.IsNullOrWhiteSpace(credential.TokenUri))
        {
            throw new InvalidOperationException("google_service_account_json_incomplete");
        }

        return credential;
    }

    private static string GetMimeType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".html" => "text/html",
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }

    private static string BuildWebViewLink(string fileId, string resourceKey)
    {
        var url = $"https://drive.google.com/file/d/{fileId}/view";
        return string.IsNullOrWhiteSpace(resourceKey)
            ? url
            : $"{url}?resourcekey={Uri.EscapeDataString(resourceKey)}";
    }

    private static string BuildDownloadLink(string fileId, string resourceKey)
    {
        var url = $"https://drive.google.com/uc?id={Uri.EscapeDataString(fileId)}&export=download";
        return string.IsNullOrWhiteSpace(resourceKey)
            ? url
            : $"{url}&resourcekey={Uri.EscapeDataString(resourceKey)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static GoogleDriveShareResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}

public sealed class GoogleServiceAccountCredential
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("private_key_id")]
    public string PrivateKeyId { get; set; } = string.Empty;

    [JsonPropertyName("private_key")]
    public string PrivateKey { get; set; } = string.Empty;

    [JsonPropertyName("client_email")]
    public string ClientEmail { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("token_uri")]
    public string TokenUri { get; set; } = string.Empty;
}
