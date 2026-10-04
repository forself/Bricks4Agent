using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.search_content 能力處理器 — 按內容搜尋
///
/// 從 InProcessDispatcher.ExecuteSearchContent() 搬遷。參數與代理工具、能力 schema 相同：
/// 搜尋文字 pattern（或 query）、目錄 directory（或 path）、檔名 file_pattern。
/// </summary>
public class SearchContentHandler : ICapabilityHandler
{
    private readonly SandboxPolicy _policy;

    public string CapabilityId => "file.search_content";

    public SearchContentHandler(string sandboxRoot)
    {
        _policy = new SandboxPolicy(sandboxRoot);
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = PayloadArgs.GetArgsElement(doc.RootElement);
            var query = PayloadArgs.GetString(root, "pattern", "query") ?? "";
            var basePath = PayloadArgs.GetString(root, "directory", "path") ?? ".";
            var filePattern = PayloadArgs.GetString(root, "file_pattern") ?? "*";

            if (!SandboxPolicy.IsFileNamePattern(filePattern))
                return Task.FromResult<(bool, string?, string?)>((false, null, SandboxPolicy.InvalidPatternError));

            var (fullPath, pathError) = _policy.Resolve(basePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>((false, null, pathError));

            if (!Directory.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"Directory not found: {basePath}"));

            var results = new List<object>();
            var files = _policy.EnumerateFilesRecursive(fullPath, filePattern)
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
}
