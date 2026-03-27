using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Handlers.Rag;

public sealed class RagImportHandler : BrokerCore.Services.IRouteHandler
{
    private readonly ILogger<RagImportHandler> _logger;
    private readonly BrokerDb _db;
    private readonly EmbeddingService? _embeddingService;

    public string Route => "rag_import";

    public RagImportHandler(
        ILogger<RagImportHandler> logger,
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

        var format = PayloadHelper.TryGetString(args, "format") ?? "json";
        var tag = PayloadHelper.TryGetString(args, "tag") ?? "";
        var data = PayloadHelper.TryGetString(args, "data") ?? "";
        var taskId = PayloadHelper.TryGetString(args, "task_id") ?? request.TaskId ?? "global";

        if (string.IsNullOrWhiteSpace(tag))
            return ExecutionResult.Fail(request.RequestId, "tag is required.");
        if (string.IsNullOrWhiteSpace(data))
            return ExecutionResult.Fail(request.RequestId, "data is required.");

        Scripts.RagIngestService.IngestResult result;
        switch (format.ToLowerInvariant())
        {
            case "json":
                result = await Scripts.RagIngestService.ImportJsonAsync(
                    data, tag, taskId, _db, _embeddingService,
                    _logger as ILogger);
                break;
            case "csv":
                result = await Scripts.RagIngestService.ImportCsvAsync(
                    data, tag, taskId, _db, _embeddingService,
                    _logger as ILogger);
                break;
            default:
                return ExecutionResult.Fail(request.RequestId, $"Unsupported format: {format}. Use 'json' or 'csv'.");
        }

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
        {
            inserted = result.Inserted,
            skipped = result.Skipped,
            embedded = result.Embedded,
            errors = result.Errors
        }));
    }
}
