using System.Text.Json;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class HighLevelInterpretationStore
{
    private const string SystemPrincipalId = "system:high-level-coordinator";
    private readonly BrokerDb _db;

    public HighLevelInterpretationStore(BrokerDb db)
    {
        _db = db;
    }

    public void Record(HighLevelInterpretationRecord record)
    {
        var documentId = BuildDocumentId(record.Channel, record.UserId);
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        var entry = new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = $"hlm.interpretation.entry.{record.InterpretationId}",
            ContentRef = JsonSerializer.Serialize(record),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-coordinator\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = "[\"high-level\",\"interpretation\"]",
            CreatedAt = record.OccurredAt.UtcDateTime
        };

        _db.Insert(entry);
    }

    public List<HighLevelInterpretationRecord> ReadLatest(string channel, string userId, int limit = 20)
    {
        var documentId = BuildDocumentId(channel, userId);
        var entries = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT @lim",
            new { docId = documentId, lim = Math.Max(1, limit) });

        var result = new List<HighLevelInterpretationRecord>();
        foreach (var entry in entries.OrderBy(e => e.Version))
        {
            if (string.IsNullOrWhiteSpace(entry.ContentRef))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<HighLevelInterpretationRecord>(entry.ContentRef);
                if (record != null)
                {
                    result.Add(record);
                }
            }
            catch
            {
            }
        }

        return result;
    }

    public static string BuildDocumentId(string channel, string userId)
        => $"hlm.interpretation.{channel}.{userId}";
}

public sealed class HighLevelInterpretationRecord
{
    public string InterpretationId { get; set; } = IdGen.New("hlm");
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string InteractionType { get; set; } = string.Empty;
    public string ParsedKind { get; set; } = string.Empty;
    public string WorkflowState { get; set; } = string.Empty;
    public string WorkflowAction { get; set; } = string.Empty;
    public bool CommandExtractionAllowed { get; set; }
    public string TrustReason { get; set; } = string.Empty;
    public string? CandidateGoal { get; set; }
    public string? TaskType { get; set; }
    public string? ProjectName { get; set; }
    public string? DraftId { get; set; }
    public string? DecisionReason { get; set; }
}
