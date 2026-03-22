using System.Text.Json;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class HighLevelMemoryStore
{
    private const string SystemPrincipalId = "system:high-level-coordinator";
    private readonly BrokerDb _db;

    public HighLevelMemoryStore(BrokerDb db)
    {
        _db = db;
    }

    public void Write(HighLevelMemoryState state)
    {
        var documentId = BuildDocumentId(state.Channel, state.UserId);
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        var entry = new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(state),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-coordinator\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = "[\"high-level\",\"memory\"]",
            CreatedAt = state.UpdatedAt.UtcDateTime
        };

        _db.Insert(entry);
    }

    public HighLevelMemoryState? ReadLatest(string channel, string userId)
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
            return JsonSerializer.Deserialize<HighLevelMemoryState>(latest.ContentRef);
        }
        catch
        {
            return null;
        }
    }

    public static string BuildDocumentId(string channel, string userId)
        => $"hlm.memory.{channel}.{userId}";
}

public sealed class HighLevelMemoryState
{
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? PreferredDisplayName { get; set; }
    public string? PreferredUserCode { get; set; }
    public string? CurrentGoal { get; set; }
    public string CurrentGoalCommitLevel { get; set; } = string.Empty;
    public string CurrentGoalSource { get; set; } = string.Empty;
    public string CurrentGoalCommitReason { get; set; } = string.Empty;
    public string LastRouteMode { get; set; } = string.Empty;
    public string WorkflowState { get; set; } = string.Empty;
    public string WorkflowAction { get; set; } = string.Empty;
    public string? PendingDraftId { get; set; }
    public bool PendingProjectName { get; set; }
    public string? ProjectName { get; set; }
    public string ProjectNameCommitLevel { get; set; } = string.Empty;
    public string ProjectNameSource { get; set; } = string.Empty;
    public string ProjectNameCommitReason { get; set; } = string.Empty;
    public string? LastTaskType { get; set; }
    public string? LastTaskId { get; set; }
    public string? LastPlanId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum HighLevelMemoryCommitLevel
{
    Candidate = 0,
    Confirmed = 1,
    SystemDerived = 2
}

public enum HighLevelMemorySource
{
    User = 0,
    ConfirmedUser = 1,
    System = 2,
    Tool = 3,
    Planner = 4
}
