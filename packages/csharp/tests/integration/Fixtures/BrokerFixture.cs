using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; private set; } = null!;

    public BrokerFixture()
    {
        // Each test run gets its own isolated SQLite file
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"broker_integration_{Guid.NewGuid():N}.db");

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
    }
}
