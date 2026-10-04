using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.list 能力處理器 — 列出目錄內容
///
/// 從 InProcessDispatcher.ExecuteListDirectory() 搬遷
/// </summary>
public class ListDirHandler : ICapabilityHandler
{
    private readonly SandboxPolicy _policy;

    public string CapabilityId => "file.list";

    public ListDirHandler(string sandboxRoot)
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
            var dirPath = root.GetProperty("path").GetString() ?? "";

            var (fullPath, pathError) = _policy.Resolve(dirPath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>((false, null, pathError));

            if (!Directory.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"Directory not found: {dirPath}"));

            var entries = new List<object>();
            // 略過 symlink／junction 與拒絕清單中的項目（SandboxPolicy.EnumerateEntries）。
            var visible = _policy.EnumerateEntries(fullPath).ToList();

            foreach (var dir in visible.OfType<DirectoryInfo>().OrderBy(d => d.Name, StringComparer.Ordinal).Take(100))
            {
                entries.Add(new { name = dir.Name, type = "directory" });
            }

            foreach (var file in visible.OfType<FileInfo>().OrderBy(f => f.Name, StringComparer.Ordinal).Take(200))
            {
                entries.Add(new { name = file.Name, type = "file", size = file.Length });
            }

            var result = JsonSerializer.Serialize(new { path = dirPath, entries });
            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"List directory error: {ex.Message}"));
        }
    }
}
