using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Services;
using BrokerCore.Services;
using BrokerCore.Models;
using Xunit;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Shared fixture that boots the Broker inside an in-memory TestServer
/// via WebApplicationFactory. Each test class that needs the running
/// Broker should implement IClassFixture&lt;BrokerFixture&gt;.
///
/// Key adaptations for the test environment:
///   - Environment set to "Testing" so production guards are skipped.
///   - Database path overridden to a per-run temp file so tests never
///     touch the real broker.db.
///   - appsettings.json is loaded from the Broker project's content root.
/// </summary>
public class BrokerFixture : IAsyncLifetime
{
    private const string LineWorkerType = "line-worker";
    private const string LineWorkerKeyId = "line-v1";
    private const string LineWorkerSecret = "line-secret";
    private const string FileWorkerType = "file-worker";
    private const string FileWorkerKeyId = "file-v1";
    private const string FileWorkerSecret = "file-secret";
    private readonly string _tempDbPath;
    private readonly string _tempAccessRoot;
    private readonly WorkerIdentityAuthOptions _workerAuthOptions;

    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; private set; } = null!;
    public string DefaultLineUserId { get; } = "line-project-user";

    public BrokerFixture()
    {
        // Each test run gets its own isolated SQLite file
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"broker_integration_{Guid.NewGuid():N}.db");
        _tempAccessRoot = Path.Combine(Path.GetTempPath(), $"broker_integration_access_{Guid.NewGuid():N}");
        _workerAuthOptions = BuildWorkerAuthOptions();

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureAppConfiguration((ctx, config) =>
                {
                    // Override the database path to the temp file
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Path"] = _tempDbPath,
                        ["HighLevelCoordinator:AccessRoot"] = _tempAccessRoot,
                        // Disable features that need external services
                        ["CacheCluster:Enabled"] = "false",
                        ["FunctionPool:Enabled"] = "false",
                        ["Embedding:Enabled"] = "false",
                        ["LineChatGateway:Enabled"] = "false",
                        ["HighLevelLlm:Enabled"] = "false",
                        ["LlmProxy:Enabled"] = "false",
                        ["WorkerAuth:Enforce"] = "true",
                        ["WorkerAuth:ClockSkewSeconds"] = _workerAuthOptions.ClockSkewSeconds.ToString(),
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
            });
    }

    public Task InitializeAsync()
    {
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task<JsonDocument> SendHighLevelLineTextAsync(string message, string? userId = null)
        => await SendHighLevelLineTextAsWorkerAsync(LineWorkerType, LineWorkerKeyId, LineWorkerSecret, message, userId);

    public async Task<HttpResponseMessage> SendHighLevelLineTextUnsignedAsync(string message, string? userId = null)
    {
        return await Client.PostAsJsonAsync("/api/v1/high-level/line/process", new
        {
            user_id = userId ?? DefaultLineUserId,
            message
        });
    }

    public async Task<JsonDocument> SendHighLevelLineTextAsWorkerAsync(
        string workerType,
        string keyId,
        string sharedSecret,
        string message,
        string? userId = null)
    {
        var response = await SendHighLevelLineTextAsWorkerRawAsync(workerType, keyId, sharedSecret, message, userId);

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public Task<HttpResponseMessage> SendHighLevelLineTextAsWorkerRawAsync(
        string workerType,
        string keyId,
        string sharedSecret,
        string message,
        string? userId = null)
    {
        var payload = new
        {
            user_id = userId ?? DefaultLineUserId,
            message
        };
        return PostSignedAsync("/api/v1/high-level/line/process", payload, workerType, keyId, sharedSecret);
    }

    public Task<HttpResponseMessage> GetPendingNotificationsAsync()
        => GetSignedAsync("/api/v1/high-level/line/notifications/pending?limit=10", LineWorkerType, LineWorkerKeyId, LineWorkerSecret);

    private async Task<HttpResponseMessage> PostSignedAsync<TBody>(string path, TBody payload, string workerType, string keyId, string sharedSecret)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        AddWorkerHeaders(request, HttpMethod.Post.Method, path, json, workerType, keyId, sharedSecret);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetSignedAsync(string pathAndQuery, string workerType, string keyId, string sharedSecret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        AddWorkerHeaders(request, HttpMethod.Get.Method, pathAndQuery.Split('?')[0], string.Empty, workerType, keyId, sharedSecret);
        return await Client.SendAsync(request);
    }

    private void AddWorkerHeaders(HttpRequestMessage request, string method, string path, string body, string workerType, string keyId, string sharedSecret)
    {
        var service = new WorkerIdentityAuthService(_workerAuthOptions, new WorkerAuthNonceStore());
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = service.SignHttp(workerType, keyId, sharedSecret, method, path, body, timestamp, nonce);
        request.Headers.Add("X-B4A-Worker-Type", workerType);
        request.Headers.Add("X-B4A-Key-Id", keyId);
        request.Headers.Add("X-B4A-Timestamp", timestamp.ToString("O"));
        request.Headers.Add("X-B4A-Nonce", nonce);
        request.Headers.Add("X-B4A-Signature", signature);
    }

    private static WorkerIdentityAuthOptions BuildWorkerAuthOptions() => new()
    {
        ClockSkewSeconds = 300,
        Credentials =
        [
            new WorkerCredentialRecord { WorkerType = LineWorkerType, KeyId = LineWorkerKeyId, SharedSecret = LineWorkerSecret, Status = "active" },
            new WorkerCredentialRecord { WorkerType = FileWorkerType, KeyId = FileWorkerKeyId, SharedSecret = FileWorkerSecret, Status = "active" }
        ],
        HttpRoutes =
        [
            new WorkerRouteRule
            {
                WorkerType = LineWorkerType,
                Paths =
                [
                    "/api/v1/high-level/line/process",
                    "/api/v1/high-level/line/notifications/pending",
                    "/api/v1/high-level/line/notifications/complete"
                ]
            }
        ]
    };

    public async Task<ProjectInterviewTaskDocument> ReadProjectInterviewRequirementsAsync(string channel, string userId)
    {
        await Task.Yield();
        using var scope = Factory.Services.CreateScope();
        var service = new ProjectInterviewStateService(scope.ServiceProvider.GetRequiredService<BrokerCore.Data.BrokerDb>());
        return await service.LoadTaskDocumentAsync(channel, userId, CancellationToken.None);
    }

    public async Task<JsonDocument> CompleteProjectInterviewToReviewAsync(string? userId = null)
    {
        var resolvedUserId = userId ?? DefaultLineUserId;
        var uniqueProjectName = "#AlphaPortal" + Guid.NewGuid().ToString("N");
        await SendHighLevelLineTextAsync("/proj", resolvedUserId);
        await SendHighLevelLineTextAsync(uniqueProjectName, resolvedUserId);
        await SendHighLevelLineTextAsync("2", resolvedUserId);
        return await SendHighLevelLineTextAsync("3", resolvedUserId);
    }

    public Task<ProjectInterviewTaskDocument> ReadProjectInterviewReviewAsync(string channel, string userId)
        => ReadProjectInterviewRequirementsAsync(channel, userId);

    public Task<ProjectInterviewVersionDag?> ReadProjectInterviewVersionDagAsync(string channel, string userId, int version)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerCore.Data.BrokerDb>();
        var entry = db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = ProjectInterviewStateService.BuildVersionDagDocumentId(channel, userId, version) })
            .FirstOrDefault();

        if (entry == null || string.IsNullOrWhiteSpace(entry.ContentRef))
            return Task.FromResult<ProjectInterviewVersionDag?>(null);

        return Task.FromResult(JsonSerializer.Deserialize<ProjectInterviewVersionDag>(entry.ContentRef));
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();

        // Clean up the temp database files
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = _tempDbPath + suffix;
            if (File.Exists(file))
            {
                try { File.Delete(file); }
                catch { /* best effort */ }
            }
        }

        if (Directory.Exists(_tempAccessRoot))
        {
            try { Directory.Delete(_tempAccessRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
