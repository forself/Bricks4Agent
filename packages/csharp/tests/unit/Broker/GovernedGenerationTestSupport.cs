using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Broker.Services;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Unit.Tests.Broker;

/// <summary>
/// 受治理生成 broker 端單元測試的共用環境：暫存的輸出根目錄與受管工作區、測試資料庫、
/// 已註冊的 LINE 使用者，以及模擬 generation-worker 寫出 zip 的輔助方法。
/// </summary>
internal sealed class GovernedGenerationTestSupport : IDisposable
{
    public const string UserId = "line-gen-user";
    public const string ProjectFolder = "ContactsDemo";

    public GovernedGenerationTestSupport()
    {
        Root = Path.Combine(Path.GetTempPath(), $"b4a-gengov-{Guid.NewGuid():N}");
        OutputRoot = Path.Combine(Root, "generation-out");
        AccessRoot = Path.Combine(Root, "access");
        Directory.CreateDirectory(OutputRoot);
        Directory.CreateDirectory(AccessRoot);

        // 資料庫建在本測試自己的暫存根目錄下，Dispose 時連同目錄一起刪除
        (Db, DatabasePath) = Helpers.TestDb.CreateIn(Root);
        Options = new GovernedGenerationOptions { OutputRoot = OutputRoot, DeadlineMinutes = 15 };
        Runs = new GovernedGenerationRunStore(Db);
        Workspace = new HighLevelLineWorkspaceService(Db, new HighLevelCoordinatorOptions { AccessRoot = AccessRoot });
        Signal = new GenerationDeliverySignal();
        SeedUserProfile(UserId);
    }

    public string Root { get; }
    public string OutputRoot { get; }
    public string AccessRoot { get; }
    public BrokerDb Db { get; }
    public string DatabasePath { get; }
    public GovernedGenerationOptions Options { get; }
    public GovernedGenerationRunStore Runs { get; }
    public HighLevelLineWorkspaceService Workspace { get; }
    public GenerationDeliverySignal Signal { get; }

    public string DocumentsRoot => Workspace.GetManagedPaths(UserId, ensureExists: true)!.DocumentsRoot;

    public void Dispose()
    {
        Db.Dispose();
        Helpers.TestDb.Delete(DatabasePath);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    public void SeedUserProfile(string userId)
    {
        var documentId = $"hlm.profile.line.{userId}";
        Db.Insert(new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = 1,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(new HighLevelUserProfile { Channel = "line", UserId = userId }),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-coordinator\"]}",
            AuthorPrincipalId = "system:high-level-coordinator",
            TaskId = SystemContextDocuments.GlobalTaskId,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>建立一個受治理生成任務（主體、任務、執行紀錄），回傳任務 id。</summary>
    public string SeedRun(string status = GovernedGenerationRunStatus.Running, DateTimeOffset? deadline = null, string containerId = "c0ffee000001")
    {
        var taskId = IdGen.New("task");
        var principalId = GovernedGenerationLauncher.BuildPrincipalId(taskId);
        Db.Insert(new Principal
        {
            PrincipalId = principalId,
            ActorType = ActorType.AI,
            DisplayName = "Governed generation agent",
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        Db.Insert(new BrokerTask
        {
            TaskId = taskId,
            TaskType = "system_scaffold",
            SubmittedBy = $"line:{UserId}",
            RiskLevel = RiskLevel.Medium,
            State = TaskState.Active,
            ScopeDescriptor = "{}",
            RuntimeDescriptor = "{}",
            AssignedPrincipalId = principalId,
            AssignedRoleId = GenerationCapabilities.ExecutorRole,
            CreatedAt = DateTime.UtcNow
        });

        var now = DateTimeOffset.UtcNow;
        Runs.Create(new GovernedGenerationRun
        {
            TaskId = taskId,
            PlanId = IdGen.New("plan"),
            DraftId = IdGen.New("draft"),
            Channel = "line",
            UserId = UserId,
            ProjectName = ProjectFolder,
            ProjectFolderName = ProjectFolder,
            PackageName = "ContactsDemo",
            OutputSlot = taskId,
            PrincipalId = principalId,
            AgentWorkerId = GovernedGenerationLauncher.BuildAgentWorkerId(taskId),
            ContainerId = containerId,
            Status = status,
            CreatedAt = now,
            DeadlineAt = deadline ?? now.AddMinutes(15)
        }, "system:test");
        return taskId;
    }

    public static string Scope(string slot) => JsonSerializer.Serialize(new
    {
        routes = new[] { GenerationCapabilities.GenerateRoute },
        output_slot = slot,
        package_name = "ContactsDemo",
        max_pages = 12,
        package = GenerationCapabilities.Package
    });

    /// <summary>模擬 worker：在 {slot}/{requestId}/ 寫出 zip，回傳它的相對路徑、sha256 與大小。</summary>
    public (string RelativePath, string Sha256, long Size) WriteWorkerPackage(string slot, string requestId, string content = "PK-fake-package")
    {
        var directory = Path.Combine(OutputRoot, slot, requestId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ContactsDemo-scaffold.zip");
        File.WriteAllText(path, content);
        var bytes = File.ReadAllBytes(path);
        return ($"{slot}/{requestId}/ContactsDemo-scaffold.zip", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
    }

    public static string WorkerPayload(string slot, string requestId, string zipPath, string sha256, long size)
        => JsonSerializer.Serialize(new
        {
            output_slot = slot,
            request_id = requestId,
            zip = new { path = zipPath, sha256, size },
            pages = new[]
            {
                new { id = "contacts-list", type = "list", field_count = 5 },
                new { id = "contact-detail", type = "detail", field_count = 5 },
                new { id = "contact-form", type = "form", field_count = 5 },
            },
            file_count = 516,
            validation_digest = new string('d', 64),
            generator_version = "definition-site/1.0.0",
            catalog_sha256 = new string('a', 64)
        });

    public static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        // Windows 沒有開發人員模式時不能建立 symlink；改用不需權限的 junction。
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>可手動推進的時鐘（watchdog 測試用）。</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
