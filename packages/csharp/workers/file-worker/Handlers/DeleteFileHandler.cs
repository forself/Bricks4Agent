using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.delete 能力處理器 — 刪除檔案
///
/// Phase 3 新增：Medium 風險，需功能池 Worker 執行
/// </summary>
public class DeleteFileHandler : ICapabilityHandler
{
    private readonly SandboxPolicy _policy;

    public string CapabilityId => "file.delete";

    public DeleteFileHandler(string sandboxRoot)
    {
        _policy = new SandboxPolicy(sandboxRoot);
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

            var (fullPath, pathError) = _policy.Resolve(filePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>((false, null, pathError));

            if (!File.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"File not found: {filePath}"));

            File.Delete(fullPath);

            var result = JsonSerializer.Serialize(new
            {
                path = filePath,
                deleted = true
            });

            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"Delete file error: {ex.Message}"));
        }
    }
}
