using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Handlers.Rag;

public sealed class RagImportWebHandler : BrokerCore.Services.IRouteHandler
{
    private readonly ILogger<RagImportWebHandler> _logger;
    private readonly BrokerDb _db;
    private readonly EmbeddingService? _embeddingService;

    public string Route => "rag_import_web";

    public RagImportWebHandler(
        ILogger<RagImportWebHandler> logger,
        BrokerDb db,
        EmbeddingService? embeddingService = null)
    {
        _logger = logger;
        _db = db;
        _embeddingService = embeddingService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);

        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var tag = PayloadHelper.TryGetString(args, "tag") ?? query;
        var taskId = PayloadHelper.TryGetString(args, "task_id") ?? request.TaskId ?? "global";
        var maxPages = 5;
        var chunkSize = 1000;
        var chunkOverlap = 100;

        if (args.TryGetProperty("max_pages", out var mp) && mp.ValueKind == JsonValueKind.Number)
            maxPages = mp.GetInt32();
        if (args.TryGetProperty("chunk_size", out var cs) && cs.ValueKind == JsonValueKind.Number)
            chunkSize = cs.GetInt32();
        if (args.TryGetProperty("chunk_overlap", out var co) && co.ValueKind == JsonValueKind.Number)
            chunkOverlap = co.GetInt32();

        List<string>? urls = null;
        if (args.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
        {
            urls = new List<string>();
            foreach (var u in urlsEl.EnumerateArray())
            {
                var s = u.GetString();
                if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
            }
        }

        if (string.IsNullOrWhiteSpace(query) && (urls == null || urls.Count == 0))
            return ExecutionResult.Fail(request.RequestId, "query or urls is required.");

        var webRequest = new Scripts.RagIngestService.WebSearchRequest
        {
            Query = query,
            Tag = tag,
            MaxPages = maxPages,
            Urls = urls,
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap
        };

        var result = await Scripts.RagIngestService.ImportFromWebAsync(
            webRequest, taskId, _db, _embeddingService,
            _logger as ILogger);

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
        {
            pages_fetched = result.PagesFetched,
            inserted = result.Inserted,
            skipped = result.Skipped,
            embedded = result.Embedded,
            errors = result.Errors
        }));
    }
}
