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
    private readonly string _sandboxRoot;

    public string CapabilityId => "file.list";

    public ListDirHandler(string sandboxRoot)
    {
        _sandboxRoot = Path.GetFullPath(sandboxRoot);
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var dirPath = doc.RootElement.GetProperty("path").GetString() ?? "";

            var fullPath = ResolveSandboxedPath(dirPath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, "Path outside sandbox."));

            if (!Directory.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"Directory not found: {dirPath}"));

            var entries = new List<object>();

            foreach (var dir in Directory.GetDirectories(fullPath).Take(100))
            {
                entries.Add(new { name = Path.GetFileName(dir), type = "directory" });
            }

            foreach (var file in Directory.GetFiles(fullPath).Take(200))
            {
                var info = new FileInfo(file);
                entries.Add(new { name = Path.GetFileName(file), type = "file", size = info.Length });
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
