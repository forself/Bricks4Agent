using System.Net;
using BrokerCore.Contracts;

namespace Broker.Services;

public sealed class AzureIisDeploymentHealthCheckService
{
    private readonly HttpClient _httpClient;

    public AzureIisDeploymentHealthCheckService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string? BuildHealthCheckUrl(AzureIisDeploymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.HealthCheckPath))
            return null;

        UriBuilder builder;
        if (!string.IsNullOrWhiteSpace(request.HealthCheckBaseUrl) &&
            Uri.TryCreate(request.HealthCheckBaseUrl, UriKind.Absolute, out var baseUri))
        {
            builder = new UriBuilder(baseUri);
        }
        else
        {
            var scheme = request.UseSsl ? "https" : "http";
            var host = request.VmHost?.Trim();
            if (string.IsNullOrWhiteSpace(host))
                return null;

            if (request.Port is not (80 or 443 or 8080 or 8443))
                return null;

            builder = new UriBuilder(scheme, host);
            if (request.Port > 0)
                builder.Port = request.Port;
        }

        var basePath = string.Equals(request.DeploymentMode, "iis_application", StringComparison.OrdinalIgnoreCase)
            ? NormalizePath(request.ApplicationPath)
            : string.Empty;
        var healthPath = NormalizePath(request.HealthCheckPath);

        builder.Path = CombinePaths(basePath, healthPath);
        return builder.Uri.ToString();
    }

    public async Task<AzureIisDeploymentHealthCheckResult> CheckAsync(
        AzureIisDeploymentRequest request,
        int maxRetries = 3,
        int retryDelayMs = 3000,
        CancellationToken cancellationToken = default)
    {
        var url = BuildHealthCheckUrl(request);
        if (string.IsNullOrWhiteSpace(url))
            return AzureIisDeploymentHealthCheckResult.Skipped();

        AzureIisDeploymentHealthCheckResult? lastResult = null;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(retryDelayMs, cancellationToken);

            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                lastResult = new AzureIisDeploymentHealthCheckResult
                {
                    Attempted = true,
                    Success = response.IsSuccessStatusCode,
                    Url = url,
                    StatusCode = (int)response.StatusCode,
                    BodySnippet = TakeSnippet(body),
                    AttemptCount = attempt + 1
                };

                if (lastResult.Success)
                    return lastResult;
            }
            catch (Exception ex)
            {
                lastResult = new AzureIisDeploymentHealthCheckResult
                {
                    Attempted = true,
                    Success = false,
                    Url = url,
                    Error = ex.Message,
                    AttemptCount = attempt + 1
                };
            }
        }

        return lastResult ?? AzureIisDeploymentHealthCheckResult.Skipped();
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim();
        if (!trimmed.StartsWith('/'))
            trimmed = "/" + trimmed;

        while (trimmed.Contains("//", StringComparison.Ordinal))
            trimmed = trimmed.Replace("//", "/", StringComparison.Ordinal);

        return trimmed.Length > 1
            ? trimmed.TrimEnd('/')
            : trimmed;
    }

    private static string CombinePaths(string left, string right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        if (string.IsNullOrWhiteSpace(normalizedLeft))
            return string.IsNullOrWhiteSpace(normalizedRight) ? "/" : normalizedRight;
        if (string.IsNullOrWhiteSpace(normalizedRight))
            return normalizedLeft;
        return $"{normalizedLeft}{normalizedRight}";
    }

    private static string TakeSnippet(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var normalized = body.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }
}

public sealed class AzureIisDeploymentHealthCheckResult
{
    public bool Attempted { get; set; }
    public bool Success { get; set; }
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string BodySnippet { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int AttemptCount { get; set; }

    public static AzureIisDeploymentHealthCheckResult Skipped()
        => new()
        {
            Attempted = false,
            Success = false
        };
}
