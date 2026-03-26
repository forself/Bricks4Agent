using System.Net;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class BrowserExecutionRuntimeService
{
    private readonly IBrowserExecutionRequestBuilder _builder;
    private readonly HttpClient _httpClient;
    private readonly ISharedContextService _sharedContextService;
    private readonly BrowserBindingService _bindingService;

    public BrowserExecutionRuntimeService(
        IBrowserExecutionRequestBuilder builder,
        HttpClient httpClient,
        ISharedContextService sharedContextService,
        BrowserBindingService bindingService)
    {
        _builder = builder;
        _httpClient = httpClient;
        _sharedContextService = sharedContextService;
        _bindingService = bindingService;
    }

    public async Task<BrowserRuntimeExecutionEnvelope> ExecuteAnonymousReadAsync(
        string toolId,
        BrowserExecutionRequestBuildInput input,
        CancellationToken cancellationToken = default)
    {
        var built = _builder.TryBuild(toolId, input);
        if (!built.Success || built.Request == null)
            return BrowserRuntimeExecutionEnvelope.Fail(built.Error ?? "browser_request_build_failed");

        var request = built.Request;
        if (!string.Equals(request.IdentityMode, "anonymous", StringComparison.Ordinal))
            return BrowserRuntimeExecutionEnvelope.Fail("browser_runtime_only_supports_anonymous");

        if (!string.Equals(request.SiteBindingMode, "public_open", StringComparison.Ordinal))
            return BrowserRuntimeExecutionEnvelope.Fail("browser_runtime_only_supports_public_open");

        if (!string.Equals(request.IntendedActionLevel, "read", StringComparison.Ordinal) &&
            !string.Equals(request.IntendedActionLevel, "navigate", StringComparison.Ordinal))
        {
            return BrowserRuntimeExecutionEnvelope.Fail("browser_runtime_only_supports_read_or_navigate");
        }

        if (!Uri.TryCreate(request.StartUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BrowserRuntimeExecutionEnvelope.Fail("browser_runtime_invalid_url");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return BrowserRuntimeExecutionEnvelope.Fail($"browser_runtime_http_{(int)response.StatusCode}");

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? request.StartUrl;
        var title = BrowserExecutionHtmlExtractor.ExtractTitle(html);
        var text = BrowserExecutionHtmlExtractor.ExtractText(html);
        var contentLength = html.Length;
        var fetchedAt = DateTimeOffset.UtcNow;

        var structuredDataJson = JsonSerializer.Serialize(new
        {
            mode = "runtime_fetch",
            fetched_at = fetchedAt,
            status_code = (int)response.StatusCode,
            content_length = contentLength
        });

        var evidenceRef = WriteEvidence(request, title, text, finalUrl, structuredDataJson, fetchedAt);
        TouchLease(request.SessionLeaseId);

        var result = BrowserExecutionResult.Ok(
            request.RequestId,
            request.ToolId,
            request.IntendedActionLevel,
            finalUrl,
            title: title,
            contentText: text,
            structuredDataJson: structuredDataJson,
            sessionLeaseId: request.SessionLeaseId,
            evidenceRef: evidenceRef);

        return BrowserRuntimeExecutionEnvelope.Ok(request, result);
    }

    private string WriteEvidence(
        BrowserExecutionRequest request,
        string title,
        string text,
        string finalUrl,
        string structuredDataJson,
        DateTimeOffset fetchedAt)
    {
        var documentId = $"browser.execution.{request.RequestId}";
        var content = JsonSerializer.Serialize(new
        {
            request_id = request.RequestId,
            tool_id = request.ToolId,
            principal_id = request.PrincipalId,
            task_id = request.TaskId,
            session_id = request.SessionId,
            final_url = finalUrl,
            title,
            content_text = text,
            structured = JsonSerializer.Deserialize<JsonElement>(structuredDataJson),
            fetched_at = fetchedAt
        });

        _sharedContextService.Write(
            request.PrincipalId,
            documentId,
            $"browser.execution.{request.RequestId}",
            content,
            "application/evidence",
            "{\"read\":[\"*\"],\"write\":[\"system:browser-runtime\", \"*\"]}",
            request.TaskId);

        return documentId;
    }

    private void TouchLease(string? sessionLeaseId)
    {
        if (string.IsNullOrWhiteSpace(sessionLeaseId))
            return;

        _bindingService.TouchSessionLease(sessionLeaseId);
    }
}

public sealed class BrowserRuntimeExecutionEnvelope
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public BrowserExecutionRequest? Request { get; set; }
    public BrowserExecutionResult? Result { get; set; }

    public static BrowserRuntimeExecutionEnvelope Ok(BrowserExecutionRequest request, BrowserExecutionResult result)
        => new()
        {
            Success = true,
            Request = request,
            Result = result
        };

    public static BrowserRuntimeExecutionEnvelope Fail(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
