using System.Text.Json;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class HighLevelExecutionIntentStore
{
    private const string SystemPrincipalId = "system:high-level-coordinator";
    private readonly BrokerDb _db;

    public HighLevelExecutionIntentStore(BrokerDb db)
    {
        _db = db;
    }

    public void Write(HighLevelExecutionIntent intent)
    {
        var documentId = BuildDocumentId(intent.Channel, intent.UserId);
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        var entry = new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = $"hlm.execution.intent.{intent.IntentId}",
            ContentRef = JsonSerializer.Serialize(intent),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-coordinator\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = "[\"high-level\",\"execution-intent\"]",
            CreatedAt = intent.CreatedAt.UtcDateTime
        };

        _db.Insert(entry);
    }

    public HighLevelExecutionIntent? ReadLatest(string channel, string userId)
    {
        var documentId = BuildDocumentId(channel, userId);
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        if (latest == null || string.IsNullOrWhiteSpace(latest.ContentRef))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HighLevelExecutionIntent>(latest.ContentRef);
        }
        catch
        {
            return null;
        }
    }

    public static string BuildDocumentId(string channel, string userId)
        => $"hlm.execution.{channel}.{userId}";
}

public sealed class HighLevelExecutionIntent
{
    public string IntentId { get; set; } = IdGen.New("xint");
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string PromotionReason { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string DraftId { get; set; } = string.Empty;
    public string ScopeDescriptor { get; set; } = "{}";
    public string RuntimeDescriptor { get; set; } = "{}";
    public string DocumentId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
