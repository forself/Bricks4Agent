using System.Text.Json;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class ProjectInterviewStateService
{
    private const string SystemPrincipalId = "system:project-interview";
    private readonly BrokerDb _db;

    public ProjectInterviewStateService(BrokerDb db)
    {
        _db = db;
    }

    public Task<ProjectInterviewTaskDocument> LoadTaskDocumentAsync(string channel, string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = BuildTaskDocumentId(channel, userId) }).FirstOrDefault();

        if (latest == null || string.IsNullOrWhiteSpace(latest.ContentRef))
            return Task.FromResult(ProjectInterviewTaskDocument.CreateEmpty(channel, userId));

        try
        {
            var deserialized = JsonSerializer.Deserialize<ProjectInterviewTaskDocument>(latest.ContentRef);
            return Task.FromResult(deserialized ?? ProjectInterviewTaskDocument.CreateEmpty(channel, userId));
        }
        catch
        {
            return Task.FromResult(ProjectInterviewTaskDocument.CreateEmpty(channel, userId));
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

    public static string BuildTaskDocumentId(string channel, string userId)
        => $"hlm.project-interview.requirements.{channel}.{userId}";
}
