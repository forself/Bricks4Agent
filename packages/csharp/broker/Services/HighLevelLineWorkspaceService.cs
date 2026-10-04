using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class HighLevelLineWorkspaceService
{
    private const string LineArtifactDocumentIdPrefix = "hlm.artifact.line.";

    private readonly BrokerDb _db;
    private readonly string _accessRoot;

    public HighLevelLineWorkspaceService(BrokerDb db, HighLevelCoordinatorOptions options)
    {
        _db = db;
        _accessRoot = ResolveAccessRoot(options.AccessRoot);
    }

    public HighLevelUserProfile? GetUserProfile(string userId)
        => LoadUserProfile("line", userId);

    public HighLevelManagedPaths? GetManagedPaths(string userId, bool ensureExists = true)
    {
        var profile = GetUserProfile(userId);
        if (profile == null)
            return null;

        var managedPaths = BuildManagedPaths("line", userId, profile, null);
        if (ensureExists)
            EnsureManagedWorkspaceLayout(managedPaths);

        return managedPaths;
    }

    public HighLevelLineNotification QueueLineNotification(string userId, string title, string body)
    {
        var notification = new HighLevelLineNotification
        {
            NotificationId = BrokerCore.IdGen.New("lntf"),
            Channel = "line",
            UserId = userId,
            Title = title,
            Body = body,
            DeliveryStatus = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        UpsertDocument(
            BuildLineNotificationDocumentId(notification.NotificationId),
            JsonSerializer.Serialize(notification),
            "application/json",
            "global");

        return notification;
    }

    public HighLevelLineArtifactRecord RecordArtifact(HighLevelLineArtifactRecord artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.ArtifactId))
            artifact.ArtifactId = BrokerCore.IdGen.New("artifact");
        if (artifact.CreatedAt == default)
            artifact.CreatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(artifact.Channel))
            artifact.Channel = "line";
        artifact.DocumentId = BuildLineArtifactDocumentId(artifact.UserId, artifact.ArtifactId);

        UpsertDocument(
            artifact.DocumentId,
            JsonSerializer.Serialize(artifact),
            "application/json",
            "global");

        return artifact;
    }

    public IReadOnlyList<HighLevelLineArtifactRecord> ListArtifacts(string userId, int limit = 50)
    {
        var take = Math.Max(1, limit);
        // 使用者 ID 只做逐字、區分大小寫的前綴比對（不用 LIKE：LIKE 有萬用字元，ASCII 也不分大小寫）。
        // 前綴相同的其他使用者（例如 "bob" 與 "bob.x"）再以紀錄本身的 UserId 排除；
        // 排除發生在 LIMIT 之後，所以分頁讀到湊滿 take 筆或讀完為止。
        var prefix = BuildLineArtifactDocumentId(userId, string.Empty);
        var items = new List<HighLevelLineArtifactRecord>();
        for (var offset = 0; items.Count < take; offset += take)
        {
            var entries = _db.Query<SharedContextEntry>(
                $"""
                SELECT * FROM shared_context_entries
                WHERE substr(document_id, 1, length(@prefix)) = @prefix AND {SystemContextDocuments.TrustedGlobalCondition()}
                ORDER BY created_at DESC, entry_id
                LIMIT @lim OFFSET @off
                """,
                new { prefix, lim = take, off = offset });

            items.AddRange(entries
                .Select(TryReadArtifact)
                .Where(item => item != null && string.Equals(item.UserId, userId, StringComparison.Ordinal))
                .Cast<HighLevelLineArtifactRecord>());

            if (entries.Count < take)
                break;
        }

        return items
            .OrderByDescending(item => item.CreatedAt)
            .Take(take)
            .ToList();
    }

    public HighLevelLineArtifactRecord? ReadArtifact(string documentId)
    {
        var entry = _db.Query<SharedContextEntry>(
            $"SELECT * FROM shared_context_entries WHERE document_id = @documentId AND {SystemContextDocuments.TrustedGlobalCondition()} ORDER BY version DESC LIMIT 1",
            new { documentId }).FirstOrDefault();

        if (entry == null || string.IsNullOrWhiteSpace(entry.ContentRef))
            return null;

        try
        {
            var item = JsonSerializer.Deserialize<HighLevelLineArtifactRecord>(entry.ContentRef);
            if (item != null && string.IsNullOrWhiteSpace(item.DocumentId))
                item.DocumentId = documentId;
            return item;
        }
        catch { return null; }
    }

    public HighLevelLineArtifactRecord? ReadArtifactById(string artifactId)
    {
        if (string.IsNullOrEmpty(artifactId))
            return null;

        // document_id = "hlm.artifact.line.{userId}.{artifactId}"：前綴與後綴都逐字、區分大小寫比對（不用 LIKE）
        var entries = _db.Query<SharedContextEntry>(
            $"""
            SELECT * FROM shared_context_entries
            WHERE substr(document_id, 1, length(@prefix)) = @prefix
              AND length(document_id) >= length(@prefix) + length(@suffix)
              AND substr(document_id, -length(@suffix)) = @suffix
              AND {SystemContextDocuments.TrustedGlobalCondition()}
            ORDER BY version DESC
            LIMIT 1
            """,
            new { prefix = LineArtifactDocumentIdPrefix, suffix = $".{artifactId}" });

        var item = entries.Count == 0 ? null : TryReadArtifact(entries[0]);
        return item != null && string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal)
            ? item
            : null;
    }

    private static HighLevelLineArtifactRecord? TryReadArtifact(SharedContextEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ContentRef))
            return null;

        try
        {
            var item = JsonSerializer.Deserialize<HighLevelLineArtifactRecord>(entry.ContentRef);
            if (item != null && string.IsNullOrWhiteSpace(item.DocumentId))
                item.DocumentId = entry.DocumentId;
            return item;
        }
        catch { return null; }
    }

    public IReadOnlyList<HighLevelLineArtifactRecord> ListAllArtifacts(string? statusFilter, int limit = 50, int offset = 0)
    {
        var entries = _db.Query<SharedContextEntry>(
            $"""
            SELECT * FROM shared_context_entries
            WHERE document_id LIKE 'hlm.artifact.line.%' AND {SystemContextDocuments.TrustedGlobalCondition()}
            ORDER BY created_at DESC
            LIMIT @lim OFFSET @off
            """,
            new { lim = Math.Max(1, limit), off = Math.Max(0, offset) });

        var items = entries
            .Select(entry =>
            {
                try
                {
                    var item = JsonSerializer.Deserialize<HighLevelLineArtifactRecord>(entry.ContentRef);
                    if (item != null && string.IsNullOrWhiteSpace(item.DocumentId))
                        item.DocumentId = entry.DocumentId;
                    return item;
                }
                catch { return null; }
            })
            .Where(item => item != null)
            .Cast<HighLevelLineArtifactRecord>();

        if (!string.IsNullOrWhiteSpace(statusFilter))
            items = items.Where(i => string.Equals(i.OverallStatus, statusFilter, StringComparison.OrdinalIgnoreCase));

        return items.OrderByDescending(i => i.CreatedAt).ToList();
    }

    public bool ResetNotificationToPending(string notificationId)
    {
        var docId = BuildLineNotificationDocumentId(notificationId);
        var entry = _db.Query<SharedContextEntry>(
            $"SELECT * FROM shared_context_entries WHERE document_id = @docId AND {SystemContextDocuments.TrustedGlobalCondition()} ORDER BY version DESC LIMIT 1",
            new { docId }).FirstOrDefault();

        if (entry == null || string.IsNullOrWhiteSpace(entry.ContentRef))
            return false;

        try
        {
            var notification = JsonSerializer.Deserialize<HighLevelLineNotification>(entry.ContentRef);
            if (notification == null)
                return false;

            notification.DeliveryStatus = "pending";
            notification.CompletedAt = null;
            notification.Error = null;

            entry.ContentRef = JsonSerializer.Serialize(notification);
            entry.Version += 1;
            _db.Update(entry);
            return true;
        }
        catch { return false; }
    }

    private HighLevelUserProfile? LoadUserProfile(string channel, string userId)
    {
        // 只採信系統元件在 global 範圍寫入的版本
        var entry = _db.Query<SharedContextEntry>(
            $"SELECT * FROM shared_context_entries WHERE document_id = @docId AND {SystemContextDocuments.TrustedGlobalCondition()} ORDER BY version DESC LIMIT 1",
            new { docId = BuildProfileDocumentId(channel, userId) }).FirstOrDefault();
        if (entry == null || string.IsNullOrWhiteSpace(entry.ContentRef))
            return null;

        try
        {
            var profile = JsonSerializer.Deserialize<HighLevelUserProfile>(entry.ContentRef);
            if (profile == null)
                return null;

            profile.Permissions ??= HighLevelUserPermissions.CreateDefault();
            return profile;
        }
        catch
        {
            return null;
        }
    }

    private HighLevelManagedPaths BuildManagedPaths(
        string channel,
        string userId,
        HighLevelUserProfile? profile,
        string? projectFolderName)
    {
        var safeChannel = SanitizePathSegment(channel, "channel");
        var userFolderName = ResolveUserFolderName(profile, userId);
        var channelRoot = Path.Combine(_accessRoot, safeChannel);
        var userRoot = Path.Combine(channelRoot, userFolderName);
        var conversationsRoot = Path.Combine(userRoot, "conversations");
        var documentsRoot = Path.Combine(userRoot, "documents");
        var projectsRoot = Path.Combine(userRoot, "projects");

        return new HighLevelManagedPaths
        {
            AccessRoot = _accessRoot,
            ChannelRoot = channelRoot,
            UserFolderName = userFolderName,
            UserRoot = userRoot,
            ConversationsRoot = conversationsRoot,
            DocumentsRoot = documentsRoot,
            ProjectsRoot = projectsRoot,
            ProjectRoot = string.IsNullOrWhiteSpace(projectFolderName)
                ? string.Empty
                : Path.Combine(projectsRoot, projectFolderName)
        };
    }

    private static void EnsureManagedWorkspaceLayout(HighLevelManagedPaths paths)
    {
        Directory.CreateDirectory(paths.AccessRoot);
        Directory.CreateDirectory(paths.ChannelRoot);
        Directory.CreateDirectory(paths.UserRoot);
        Directory.CreateDirectory(paths.ConversationsRoot);
        Directory.CreateDirectory(paths.DocumentsRoot);
        Directory.CreateDirectory(paths.ProjectsRoot);

        if (!string.IsNullOrWhiteSpace(paths.ProjectRoot))
            Directory.CreateDirectory(paths.ProjectRoot);
    }

    private void UpsertDocument(string documentId, string contentRef, string contentType, string taskId)
    {
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @documentId ORDER BY version DESC LIMIT 1",
            new { documentId }).FirstOrDefault();

        // 最新版本不是系統元件寫入的（或不存在）時另寫新版本，不覆寫他人的版本
        if (latest == null ||
            !latest.AuthorPrincipalId.StartsWith("system:", StringComparison.Ordinal) ||
            !string.Equals(latest.TaskId, taskId, StringComparison.Ordinal))
        {
            _db.Insert(new SharedContextEntry
            {
                EntryId = BrokerCore.IdGen.New("ctx"),
                TaskId = taskId,
                DocumentId = documentId,
                Key = documentId,
                ContentRef = contentRef,
                ContentType = contentType,
                AuthorPrincipalId = "system:high-level-workspace",
                Acl = """{"read":["*"],"write":["system:high-level-workspace"]}""",
                Version = (latest?.Version ?? 0) + 1,
                ParentVersion = latest?.Version,
                CreatedAt = DateTime.UtcNow
            });
            return;
        }

        latest.ContentRef = contentRef;
        latest.ContentType = contentType;
        latest.Version += 1;
        _db.Update(latest);
    }

    private static string ResolveUserFolderName(HighLevelUserProfile? profile, string userId)
    {
        if (!string.IsNullOrWhiteSpace(profile?.PreferredUserCode))
            return SanitizePathSegment(profile.PreferredUserCode, "user");

        return SanitizePathSegment(userId, "user");
    }

    private static string SanitizePathSegment(string value, string fallback)
    {
        var sanitized = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalid, '-');

        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        sanitized = sanitized.TrimEnd('.', ' ');
        if (sanitized.Length > 80)
            sanitized = sanitized[..80].TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string ResolveAccessRoot(string configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
            return HighLevelCoordinatorDefaults.DefaultAccessRoot;

        var expanded = Environment.ExpandEnvironmentVariables(configuredRoot.Trim());
        if (!Path.IsPathRooted(expanded))
            throw new InvalidOperationException(
                "HighLevelCoordinator:AccessRoot must be an absolute path. Relative paths are not allowed.");

        return Path.GetFullPath(expanded);
    }

    private static string BuildProfileDocumentId(string channel, string userId)
        => $"hlm.profile.{channel}.{userId}";

    private static string BuildLineNotificationDocumentId(string notificationId)
        => $"hlm.notify.line.{notificationId}";

    private static string BuildLineArtifactDocumentId(string userId, string artifactId)
        => $"{LineArtifactDocumentIdPrefix}{userId}.{artifactId}";
}
