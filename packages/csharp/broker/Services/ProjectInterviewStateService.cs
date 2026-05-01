using System.Text.Json;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class ProjectInterviewStateService
{
    private const string SystemPrincipalId = "system:project-interview";
    private const int DefaultActiveSessionTimeoutMinutes = 60;
    private readonly BrokerDb _db;
    private readonly TimeSpan _activeSessionTimeout;

    public ProjectInterviewStateService(BrokerDb db, IConfiguration? configuration = null)
    {
        _db = db;
        var configuredTimeoutMinutes = configuration?.GetValue<int?>("ProjectInterview:ActiveSessionTimeoutMinutes");
        _activeSessionTimeout = configuredTimeoutMinutes.HasValue && configuredTimeoutMinutes.Value > 0
            ? TimeSpan.FromMinutes(configuredTimeoutMinutes.Value)
            : TimeSpan.FromMinutes(DefaultActiveSessionTimeoutMinutes);
    }

    public async Task<ProjectInterviewTaskDocument> LoadTaskDocumentAsync(string channel, string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = BuildTaskDocumentId(channel, userId) }).FirstOrDefault();

        if (latest == null || string.IsNullOrWhiteSpace(latest.ContentRef))
            return ProjectInterviewTaskDocument.CreateEmpty(channel, userId);

        try
        {
            var deserialized = JsonSerializer.Deserialize<ProjectInterviewTaskDocument>(latest.ContentRef);
            if (deserialized == null)
                return ProjectInterviewTaskDocument.CreateEmpty(channel, userId);

            if (deserialized.IsActiveSession && IsStale(latest.CreatedAt))
            {
                var expired = deserialized
                    .WithSessionState(deserialized.SessionState with { CurrentPhase = ProjectInterviewPhase.Expired })
                    .ClearPendingOptions();

                await SaveTaskDocumentAsync(expired, cancellationToken);
                return expired;
            }

            return deserialized;
        }
        catch
        {
            return ProjectInterviewTaskDocument.CreateEmpty(channel, userId);
        }
    }

    public Task SaveTaskDocumentAsync(ProjectInterviewTaskDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var documentId = BuildTaskDocumentId(document.Channel, document.UserId);
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        _db.Insert(new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(document),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:project-interview\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = "[\"high-level\",\"project-interview\",\"requirements\"]",
            CreatedAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task SaveVersionDagAsync(string channel, string userId, int version, ProjectInterviewVersionDag dag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var documentId = BuildVersionDagDocumentId(channel, userId, version);
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        _db.Insert(new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(dag),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:project-interview\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = "[\"high-level\",\"project-interview\",\"version-dag\"]",
            CreatedAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public static string BuildTaskDocumentId(string channel, string userId)
        => $"hlm.project-interview.requirements.{channel}.{userId}";

    public static string BuildVersionDagDocumentId(string channel, string userId, int version)
        => $"hlm.project-interview.version-graph.{channel}.{userId}.{version}";

    private bool IsStale(DateTime createdAtUtc)
    {
        var age = DateTime.UtcNow - DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        return age >= _activeSessionTimeout;
    }
}
