using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class HighLevelWorkflowAdminService
{
    private readonly BrokerDb _db;

    public HighLevelWorkflowAdminService(BrokerDb db)
    {
        _db = db;
    }

    public WorkflowAdminSnapshot GetSnapshot(int limit = 20)
        => new()
        {
            Tasks = _db.Query<BrokerTask>(
                "SELECT * FROM broker_tasks ORDER BY created_at DESC LIMIT @limit",
                new { limit = NormalizeLimit(limit) }),
            Plans = _db.Query<Plan>(
                "SELECT * FROM plans ORDER BY created_at DESC LIMIT @limit",
                new { limit = NormalizeLimit(limit) }),
            ExecutionIntents = ListJsonDocuments<HighLevelExecutionIntentSummary>("hlm.execution.%", limit),
            Handoffs = ListJsonDocuments<HighLevelHandoffSummary>("hlm.handoff.%", limit)
        };

    public HighLevelExecutionIntentDetail? ReadExecutionIntent(string documentId)
        => ReadJsonDocument<HighLevelExecutionIntentDetail>(documentId);

    public HighLevelHandoffDetail? ReadHandoff(string documentId)
        => ReadJsonDocument<HighLevelHandoffDetail>(documentId);

    private List<T> ListJsonDocuments<T>(string pattern, int limit) where T : WorkflowDocumentBase, new()
    {
        var entries = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id LIKE @pattern ORDER BY created_at DESC LIMIT @limit",
            new { pattern, limit = NormalizeLimit(limit) });

        var items = new List<T>();
        foreach (var entry in entries)
        {
            var item = TryReadJsonDocument<T>(entry);
            if (item == null)
                continue;

            item.DocumentId = entry.DocumentId;
            item.CreatedAt = entry.CreatedAt;
            items.Add(item);
        }

        return items;
    }

    private T? ReadJsonDocument<T>(string documentId) where T : WorkflowDocumentBase, new()
    {
        var entry = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        return entry == null ? null : TryReadJsonDocument<T>(entry);
    }

    private static T? TryReadJsonDocument<T>(SharedContextEntry entry) where T : WorkflowDocumentBase, new()
    {
        if (string.IsNullOrWhiteSpace(entry.ContentRef))
            return null;

        try
        {
            using var document = JsonDocument.Parse(entry.ContentRef);
            var root = document.RootElement;
            var item = new T
            {
                DocumentId = entry.DocumentId,
                CreatedAt = entry.CreatedAt
            };

            if (typeof(T) == typeof(HighLevelExecutionIntentSummary) || typeof(T) == typeof(HighLevelExecutionIntentDetail))
            {
                var intent = item as HighLevelExecutionIntentDetail ?? new HighLevelExecutionIntentDetail();
                intent.IntentId = ReadString(root, "IntentId");
                intent.Channel = ReadString(root, "Channel");
                intent.UserId = ReadString(root, "UserId");
                intent.Stage = ReadString(root, "Stage");
                intent.PromotionReason = ReadString(root, "PromotionReason");
                intent.Goal = ReadString(root, "Goal");
                intent.TaskType = ReadString(root, "TaskType");
                intent.ProjectName = ReadOptionalString(root, "ProjectName");
                intent.DraftId = ReadString(root, "DraftId");
                intent.RuntimeDescriptor = ReadString(root, "RuntimeDescriptor");
                intent.ScopeDescriptor = ReadString(root, "ScopeDescriptor");
                intent.RequestedExecutionModel = root.TryGetProperty("RequestedExecutionModel", out var model)
                    ? model.Clone()
                    : JsonDocument.Parse("null").RootElement.Clone();

                return (T)(object)intent;
            }

            if (typeof(T) == typeof(HighLevelHandoffSummary) || typeof(T) == typeof(HighLevelHandoffDetail))
            {
                var handoff = item as HighLevelHandoffDetail ?? new HighLevelHandoffDetail();
                handoff.TaskId = ReadString(root, "TaskId");
                handoff.PlanId = ReadString(root, "PlanId");
                handoff.Channel = ReadString(root, "Channel");
                handoff.UserId = ReadString(root, "UserId");
                handoff.TaskType = ReadString(root, "TaskType");
                handoff.Goal = ReadString(root, "Goal");
                handoff.ProjectName = ReadOptionalString(root, "ProjectName");
                handoff.Payload = root.Clone();
                return (T)(object)handoff;
            }

            return item;
        }
        catch
        {
            return null;
        }
    }

    private static int NormalizeLimit(int limit)
        => limit <= 0 ? 20 : Math.Min(limit, 100);

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadOptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}

public sealed class WorkflowAdminSnapshot
{
    public IReadOnlyList<BrokerTask> Tasks { get; set; } = [];
    public IReadOnlyList<Plan> Plans { get; set; } = [];
    public IReadOnlyList<HighLevelExecutionIntentSummary> ExecutionIntents { get; set; } = [];
    public IReadOnlyList<HighLevelHandoffSummary> Handoffs { get; set; } = [];
}

public abstract class WorkflowDocumentBase
{
    public string DocumentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class HighLevelExecutionIntentSummary : WorkflowDocumentBase
{
    public string IntentId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
}

public sealed class HighLevelExecutionIntentDetail : HighLevelExecutionIntentSummary
{
    public string PromotionReason { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public string RuntimeDescriptor { get; set; } = "{}";
    public string ScopeDescriptor { get; set; } = "{}";
    public JsonElement RequestedExecutionModel { get; set; }
}

public class HighLevelHandoffSummary : WorkflowDocumentBase
{
    public string TaskId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
}

public sealed class HighLevelHandoffDetail : HighLevelHandoffSummary
{
    public JsonElement Payload { get; set; }
}
