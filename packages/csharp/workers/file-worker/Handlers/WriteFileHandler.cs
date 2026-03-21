using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.write 能力處理器 — 寫入檔案
///
/// Phase 3 新增：Medium 風險，需功能池 Worker 執行
/// </summary>
public class WriteFileHandler : ICapabilityHandler
{
    private readonly string _sandboxRoot;

    public string CapabilityId => "file.write";

    public WriteFileHandler(string sandboxRoot)
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
            var filePath = root.GetProperty("path").GetString() ?? "";
            var content = root.GetProperty("content").GetString() ?? "";

            var fullPath = ResolveSandboxedPath(filePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, "Path outside sandbox."));

            // 確保目錄存在
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content);

            var result = JsonSerializer.Serialize(new
            {
                path = filePath,
                bytes_written = content.Length,
                success = true
            });

            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"Write file error: {ex.Message}"));
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
