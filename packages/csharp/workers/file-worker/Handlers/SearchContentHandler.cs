using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.search_content 能力處理器 — 按內容搜尋
///
/// 從 InProcessDispatcher.ExecuteSearchContent() 搬遷
/// </summary>
public class SearchContentHandler : ICapabilityHandler
{
    private readonly string _sandboxRoot;

    public string CapabilityId => "file.search_content";

    public SearchContentHandler(string sandboxRoot)
    {
        _sandboxRoot = Path.GetFullPath(sandboxRoot);
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement.TryGetProperty("args", out var argsEl)
                ? argsEl : doc.RootElement;
            var query = root.GetProperty("query").GetString() ?? "";
            var basePath = root.TryGetProperty("path", out var p)
                ? p.GetString() ?? "." : ".";
            var filePattern = root.TryGetProperty("file_pattern", out var fp)
                ? fp.GetString() ?? "*" : "*";

            var fullPath = ResolveSandboxedPath(basePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, "Path outside sandbox."));

            if (!Directory.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"Directory not found: {basePath}"));

            var results = new List<object>();
            var files = Directory.GetFiles(fullPath, filePattern, SearchOption.AllDirectories)
                .Take(500);

            foreach (var file in files)
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new
                            {
                                file = Path.GetRelativePath(fullPath, file).Replace('\\', '/'),
                                line = i + 1,
                                content = lines[i].Length > 200 ? lines[i][..200] + "..." : lines[i]
                            });

                            if (results.Count >= 50) break;
                        }
                    }
                    if (results.Count >= 50) break;
                }
                catch
                {
                    // 跳過無法讀取的檔案
                }
            }

            var result = JsonSerializer.Serialize(new { query, basePath, matches = results });
            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"Search content error: {ex.Message}"));
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
