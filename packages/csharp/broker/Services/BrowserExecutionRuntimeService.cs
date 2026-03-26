using System.Net;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class BrowserExecutionRuntimeService
{
    private readonly IBrowserExecutionRequestBuilder _builder;
    private readonly HttpClient _httpClient;
    private readonly ISharedContextService _sharedContextService;
    private readonly BrowserBindingService _bindingService;
    private readonly BrokerDb _db;

    public BrowserExecutionRuntimeService(
        IBrowserExecutionRequestBuilder builder,
        HttpClient httpClient,
        BrokerDb db,
        ISharedContextService sharedContextService,
        BrowserBindingService bindingService)
    {
        _builder = builder;
        _httpClient = httpClient;
        _db = db;
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

    public IReadOnlyList<BrowserExecutionEvidenceSummary> ListRecentExecutions(int limit = 20, string? principalId = null)
    {
        if (limit <= 0)
            limit = 20;

        var sql = """
            SELECT *
              FROM shared_context_entries
             WHERE document_id LIKE 'browser.execution.%'
        """;
        var args = new Dictionary<string, object?>
        {
            ["limit"] = limit
        };

        if (!string.IsNullOrWhiteSpace(principalId))
        {
            sql += " AND author_principal_id = @principalId";
            args["principalId"] = principalId;
        }

        sql += " ORDER BY created_at DESC LIMIT @limit";
        var entries = _db.Query<SharedContextEntry>(sql, args);
        var summaries = new List<BrowserExecutionEvidenceSummary>();
        foreach (var entry in entries)
        {
            var evidence = TryReadEvidenceEntry(entry);
            if (evidence == null)
                continue;

            summaries.Add(new BrowserExecutionEvidenceSummary
            {
                DocumentId = entry.DocumentId,
                RequestId = evidence.RequestId,
                ToolId = evidence.ToolId,
                PrincipalId = evidence.PrincipalId,
                TaskId = evidence.TaskId,
                FinalUrl = evidence.FinalUrl,
                Title = evidence.Title,
                FetchedAt = evidence.FetchedAt,
                CreatedAt = entry.CreatedAt
            });
        }

        return summaries;
    }

    public BrowserExecutionEvidenceDetail? ReadExecution(string documentId)
    {
        var entry = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        return entry == null ? null : TryReadEvidenceEntry(entry);
    }

    private static BrowserExecutionEvidenceDetail? TryReadEvidenceEntry(SharedContextEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ContentRef))
            return null;

        try
        {
            using var document = JsonDocument.Parse(entry.ContentRef);
            var root = document.RootElement;
            return new BrowserExecutionEvidenceDetail
            {
                RequestId = ReadString(root, "request_id"),
                ToolId = ReadString(root, "tool_id"),
                PrincipalId = ReadString(root, "principal_id"),
                TaskId = ReadOptionalString(root, "task_id"),
                SessionId = ReadString(root, "session_id"),
                FinalUrl = ReadString(root, "final_url"),
                Title = ReadOptionalString(root, "title"),
                ContentText = ReadOptionalString(root, "content_text"),
                Structured = root.TryGetProperty("structured", out var structured)
                    ? structured.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone(),
                FetchedAt = ReadDateTimeOffset(root, "fetched_at")
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadOptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset ReadDateTimeOffset(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return default;

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : default;
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

public sealed class BrowserExecutionEvidenceSummary
{
    public string DocumentId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string FinalUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class BrowserExecutionEvidenceDetail
{
    public string RequestId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string FinalUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public JsonElement Structured { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
