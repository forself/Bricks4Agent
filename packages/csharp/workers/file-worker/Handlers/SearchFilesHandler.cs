using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.search_name 能力處理器 — 按檔名搜尋
///
/// 從 InProcessDispatcher.ExecuteSearchFiles() 搬遷。參數與代理工具、能力 schema 相同：
/// 檔名 pattern（預設 *）、目錄 directory（或 path）。
/// </summary>
public class SearchFilesHandler : ICapabilityHandler
{
    private readonly SandboxPolicy _policy;

    public string CapabilityId => "file.search_name";

    public SearchFilesHandler(string sandboxRoot)
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
            var pattern = PayloadArgs.GetString(root, "pattern") ?? "*";
            var basePath = PayloadArgs.GetString(root, "directory", "path") ?? ".";

            if (!SandboxPolicy.IsFileNamePattern(pattern))
                return Task.FromResult<(bool, string?, string?)>((false, null, SandboxPolicy.InvalidPatternError));

            var (fullPath, pathError) = _policy.Resolve(basePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>((false, null, pathError));

            if (!Directory.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"Directory not found: {basePath}"));

            var files = _policy.EnumerateFilesRecursive(fullPath, pattern)
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
}
