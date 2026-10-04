using System.Text.Json;
using WorkerSdk;

namespace FileWorker.Handlers;

/// <summary>
/// file.read 能力處理器 — 讀取檔案內容
///
/// 從 InProcessDispatcher.ExecuteReadFile() 搬遷
/// </summary>
public class ReadFileHandler : ICapabilityHandler
{
    private readonly SandboxPolicy _policy;

    public string CapabilityId => "file.read";

    public ReadFileHandler(string sandboxRoot)
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
            var filePath = root.GetProperty("path").GetString() ?? "";

            var (fullPath, pathError) = _policy.Resolve(filePath);
            if (fullPath == null)
                return Task.FromResult<(bool, string?, string?)>((false, null, pathError));

            if (!File.Exists(fullPath))
                return Task.FromResult<(bool, string?, string?)>(
                    (false, null, $"File not found: {filePath}"));

            var content = File.ReadAllText(fullPath);

            // 截斷過長內容（限制 100KB）
            if (content.Length > 102400)
                content = content[..102400] + "\n... [truncated]";

            var result = JsonSerializer.Serialize(new
            {
                path = filePath,
                content,
                size = new FileInfo(fullPath).Length
            });

            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"Read file error: {ex.Message}"));
        }
    }
}
