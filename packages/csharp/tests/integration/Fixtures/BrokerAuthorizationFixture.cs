using System.Text;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Broker host used by the HTTP authorization regression tests. Compared with
/// <see cref="BrokerFixture"/> it:
///   - keeps the SQLite database and the high-level access root in a per-fixture temp
///     directory that is deleted on dispose (both are read before the host is built, so they
///     are passed with UseSetting; ConfigureAppConfiguration does not reach that stage);
///   - turns the per-IP rate limiter off so the endpoint matrix is not throttled;
///   - registers <see cref="RemoteAddressStartupFilter"/> so a test can choose the caller address.
/// Worker authentication is configured the same way as in BrokerFixture (enforced, line-worker
/// routes), with credentials of its own. <see cref="WorkerAuthNotEnforcedFixture"/> is the same
/// host with worker authentication switched off; the worker options are resolved from
/// configuration when the host runs, so ConfigureAppConfiguration does reach them.
/// </summary>
public class BrokerAuthorizationFixture : IAsyncLifetime
{
    public const string LineWorkerType = "line-worker";

    private const string LineWorkerKeyId = "authz-line-v1";
    private const string LineWorkerSecret = "authz-regression-line-worker-credential";
    private const string FileWorkerType = "file-worker";
    private const string FileWorkerKeyId = "authz-file-v1";
    private const string FileWorkerSecret = "authz-regression-file-worker-credential";

    private const int CleanupAttempts = 20;
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _workDirectory;
    private readonly WebApplicationFactory<Program> _rootFactory;

    public BrokerAuthorizationFixture()
        : this(enforceWorkerAuth: true)
    {
    }

    protected BrokerAuthorizationFixture(bool enforceWorkerAuth)
    {
        WorkerAuthEnforced = enforceWorkerAuth;
        _workDirectory = Path.Combine(Path.GetTempPath(), $"b4a-authz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        var databasePath = Path.Combine(_workDirectory, "broker.db");
        var accessRoot = Path.Combine(_workDirectory, "access");

        _rootFactory = new WebApplicationFactory<Program>();
        Factory = _rootFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("Database:Path", databasePath);
            builder.UseSetting("HighLevelCoordinator:AccessRoot", accessRoot);
            builder.UseSetting("Broker:IpRateLimit:Enabled", "false");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WorkerAuth:Enforce"] = enforceWorkerAuth ? "true" : "false",
                    ["WorkerAuth:ClockSkewSeconds"] = "300",
                    ["WorkerAuth:Credentials:0:WorkerType"] = LineWorkerType,
                    ["WorkerAuth:Credentials:0:KeyId"] = LineWorkerKeyId,
                    ["WorkerAuth:Credentials:0:SharedSecret"] = LineWorkerSecret,
                    ["WorkerAuth:Credentials:0:Status"] = "active",
                    ["WorkerAuth:Credentials:1:WorkerType"] = FileWorkerType,
                    ["WorkerAuth:Credentials:1:KeyId"] = FileWorkerKeyId,
                    ["WorkerAuth:Credentials:1:SharedSecret"] = FileWorkerSecret,
                    ["WorkerAuth:Credentials:1:Status"] = "active",
                    ["WorkerAuth:HttpRoutes:0:WorkerType"] = LineWorkerType,
                    ["WorkerAuth:HttpRoutes:0:Paths:0"] = "/api/v1/high-level/line/process",
                    ["WorkerAuth:HttpRoutes:0:Paths:1"] = "/api/v1/high-level/line/notifications/pending",
                    ["WorkerAuth:HttpRoutes:0:Paths:2"] = "/api/v1/high-level/line/notifications/complete",
                });
            });

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, RemoteAddressStartupFilter>());
        });
    }

    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => Factory.Services;
    public bool WorkerAuthEnforced { get; }

    public Task InitializeAsync()
    {
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        return Task.CompletedTask;
    }

    public void SeedPrincipal(string principalId)
    {
        var db = Services.GetRequiredService<BrokerDb>();
        db.Insert(new Principal
        {
            PrincipalId = principalId,
            ActorType = ActorType.AI,
            DisplayName = "authorization regression",
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
    }

    public void SeedTask(
        string taskId,
        string taskType,
        string submittedBy,
        string? assignedPrincipalId,
        string? assignedRoleId)
    {
        var db = Services.GetRequiredService<BrokerDb>();
        db.Insert(new BrokerTask
        {
            TaskId = taskId,
            TaskType = taskType,
            SubmittedBy = submittedBy,
            RiskLevel = RiskLevel.Low,
            State = TaskState.Active,
            ScopeDescriptor = "{}",
            RuntimeDescriptor = "{}",
            AssignedPrincipalId = assignedPrincipalId,
            AssignedRoleId = assignedRoleId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public BrokerTask? FindTask(string taskId)
        => Services.GetRequiredService<BrokerDb>().Get<BrokerTask>(taskId);

    public BrokerDb Db => Services.GetRequiredService<BrokerDb>();

    public ContainerSession? FindSession(string sessionId)
        => Services.GetRequiredService<ISessionService>().GetSession(sessionId);

    /// <summary>
    /// Seeds an active principal and a task assigned to it with <paramref name="roleId"/>, then opens a session
    /// for them through <paramref name="client"/>, so flows that look at the principal and task records
    /// (for example token renewal on heartbeat) see real ones.
    /// </summary>
    public BrokerTestSession OpenSeededSession(EncryptedBrokerClient client, string roleId, string taskType = "query")
    {
        var principalId = EncryptedBrokerClient.NewId("prn_authz_seeded");
        var taskId = EncryptedBrokerClient.NewId("task_authz_seeded");
        SeedPrincipal(principalId);
        SeedTask(taskId, taskType, submittedBy: principalId, assignedPrincipalId: principalId, assignedRoleId: roleId);
        return client.OpenSession(roleId, principalId, taskId);
    }

    /// <summary>Creates a draft plan for <paramref name="taskId"/> through the broker's plan service.</summary>
    public Plan CreatePlan(string taskId, string submittedBy)
        => Services.GetRequiredService<IPlanService>()
            .CreatePlan(taskId, submittedBy, "authorization regression", description: null);

    public Plan? FindPlan(string planId)
        => Services.GetRequiredService<IPlanService>().GetPlan(planId);

    public int CountPlans(string taskId)
        => Services.GetRequiredService<BrokerDb>()
            .Query<Plan>("SELECT * FROM plans WHERE task_id = @taskId", new { taskId })
            .Count;

    /// <summary>Stores a completed execution request that belongs to <paramref name="taskId"/>.</summary>
    public string SeedExecutionRequest(string taskId, string principalId)
    {
        var requestId = $"req_authz_{Guid.NewGuid():N}";
        Services.GetRequiredService<BrokerDb>().Insert(new ExecutionRequest
        {
            RequestId = requestId,
            TaskId = taskId,
            SessionId = $"ses_authz_{Guid.NewGuid():N}",
            PrincipalId = principalId,
            CapabilityId = "file.read",
            Intent = "authorization regression",
            RequestPayload = "{}",
            ExecutionState = ExecutionState.Succeeded,
            TraceId = $"trace_authz_{Guid.NewGuid():N}",
            IdempotencyKey = $"idem_authz_{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        return requestId;
    }

    /// <summary>
    /// Stores a shared-context entry directly, bypassing the write endpoint and its ACL validation,
    /// so a test can also place entries the endpoint would refuse.
    /// </summary>
    public void SeedContextEntry(
        string documentId,
        string key,
        string contentRef,
        string acl,
        string authorPrincipalId,
        string? taskId,
        int version = 1)
    {
        Services.GetRequiredService<BrokerDb>().Insert(new SharedContextEntry
        {
            EntryId = $"ctx_authz_{Guid.NewGuid():N}",
            DocumentId = documentId,
            Version = version,
            ParentVersion = version > 1 ? version - 1 : null,
            Key = key,
            ContentRef = contentRef,
            ContentType = "application/json",
            Acl = acl,
            AuthorPrincipalId = authorPrincipalId,
            TaskId = taskId,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>Records a LINE artifact for <paramref name="userId"/> through the broker's workspace service.</summary>
    public Broker.Services.HighLevelLineArtifactRecord RecordLineArtifact(string userId)
        => Services.GetRequiredService<Broker.Services.HighLevelLineWorkspaceService>().RecordArtifact(new Broker.Services.HighLevelLineArtifactRecord
        {
            UserId = userId,
            Source = "authorization-regression",
            FileName = "authorization-regression.txt",
            Format = "txt",
            Success = true,
            OverallStatus = "completed"
        });

    public IReadOnlyList<SharedContextEntry> FindContextEntries(string documentId)
        => Services.GetRequiredService<BrokerDb>()
            .Query<SharedContextEntry>(
                "SELECT * FROM shared_context_entries WHERE document_id = @documentId ORDER BY version",
                new { documentId });

    public int CurrentEpoch()
        => Services.GetRequiredService<IRevocationService>().GetCurrentEpoch();

    /// <summary>
    /// Sends a request signed with this fixture's line-worker credential. The signature covers
    /// the method, the path without query and the exact body, as the line worker signs it.
    /// </summary>
    public async Task<BrokerReply> SendLineWorkerSignedAsync(HttpMethod method, string pathAndQuery, string? jsonBody = null)
    {
        var path = pathAndQuery.Split('?')[0];
        var body = jsonBody ?? string.Empty;
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signer = new WorkerIdentityAuthService(new WorkerIdentityAuthOptions(), new WorkerAuthNonceStore());
        var signature = signer.SignHttp(LineWorkerType, LineWorkerKeyId, LineWorkerSecret, method.Method, path, body, timestamp, nonce);

        using var request = new HttpRequestMessage(method, pathAndQuery);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        request.Headers.Add(WorkerIdentityHeaders.WorkerType, LineWorkerType);
        request.Headers.Add(WorkerIdentityHeaders.KeyId, LineWorkerKeyId);
        request.Headers.Add(WorkerIdentityHeaders.Timestamp, timestamp.ToString("O"));
        request.Headers.Add(WorkerIdentityHeaders.Nonce, nonce);
        request.Headers.Add(WorkerIdentityHeaders.Signature, signature);

        using var response = await Client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        return new BrokerReply(response.StatusCode, raw, decrypted: false);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await Factory.DisposeAsync();
        await _rootFactory.DisposeAsync();

        // The broker opens pooled SQLite connections; release them so the database file can be removed.
        SqliteConnection.ClearAllPools();
        await DeleteWorkDirectoryAsync();
    }

    private async Task DeleteWorkDirectoryAsync()
    {
        for (var attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(_workDirectory))
                {
                    Directory.Delete(_workDirectory, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == CleanupAttempts)
                {
                    throw new IOException(
                        $"Could not remove the authorization test work directory '{_workDirectory}'.", ex);
                }

                SqliteConnection.ClearAllPools();
                await Task.Delay(CleanupDelay);
            }
        }
    }
}

/// <summary>
/// The authorization test host with <c>WorkerAuth:Enforce=false</c>: worker signatures are not verified,
/// so endpoints that accept only a verified worker signature must reject every request.
/// </summary>
public sealed class WorkerAuthNotEnforcedFixture : BrokerAuthorizationFixture
{
    public WorkerAuthNotEnforcedFixture()
        : base(enforceWorkerAuth: false)
    {
    }
}
