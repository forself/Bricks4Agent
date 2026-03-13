using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.search_name 能力處理器 — 按檔名搜尋
///
/// 從 InProcessDispatcher.ExecuteSearchFiles() 搬遷
/// </summary>
public class SearchFilesHandler : ICapabilityHandler
{
    private readonly string _sandboxRoot;

    public string CapabilityId => "file.search_name";

    public SearchFilesHandler(string sandboxRoot)
    {
        _sandboxRoot = Path.GetFullPath(sandboxRoot);
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var pattern = doc.RootElement.GetProperty("pattern").GetString() ?? "*";
            var basePath = doc.RootElement.TryGetProperty("path", out var p)
                ? p.GetString() ?? "." : ".";

            var fullPath = ResolveSandboxedPath(basePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, "Path outside sandbox."));

            if (!Directory.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"Directory not found: {basePath}"));

            var files = Directory.GetFiles(fullPath, pattern, SearchOption.AllDirectories)
                .Take(100)
                .Select(f => Path.GetRelativePath(fullPath, f).Replace('\\', '/'))
                .ToList();

            var result = JsonSerializer.Serialize(new { basePath, pattern, matches = files });
            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"Search files error: {ex.Message}"));
        }
    }

    private string? ResolveSandboxedPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_sandboxRoot, path));
            if (!fullPath.StartsWith(_sandboxRoot, StringComparison.OrdinalIgnoreCase))
                return null;
            return fullPath;
        }
        catch { return null; }
    }
}
