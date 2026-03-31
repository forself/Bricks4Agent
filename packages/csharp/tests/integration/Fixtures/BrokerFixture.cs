using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Services;
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
    private readonly string _tempDbPath;
    private readonly string _tempAccessRoot;

    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; private set; } = null!;
    public string DefaultLineUserId { get; } = "line-project-user";

    public BrokerFixture()
    {
        // Each test run gets its own isolated SQLite file
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"broker_integration_{Guid.NewGuid():N}.db");
        _tempAccessRoot = Path.Combine(Path.GetTempPath(), $"broker_integration_access_{Guid.NewGuid():N}");

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
    {
        var response = await Client.PostAsJsonAsync("/api/v1/high-level/line/process", new
        {
            user_id = userId ?? DefaultLineUserId,
            message
        });

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

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
