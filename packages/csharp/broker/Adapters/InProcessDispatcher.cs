using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;

namespace Broker.Adapters;

/// <summary>
/// Phase 1 Inline 執行轉發器 —— 僅處理低風險讀取操作
///
/// 設計：
/// - 在 broker 進程內直接執行（不跨進程/跨網路）
/// - 僅實作 file.read、file.list、file.search_name、file.search_content
/// - 所有路徑受沙箱限制
///
/// Phase 2 替換為：
/// - HttpDispatcher（呼叫 Agent Container 的 REST API）
/// - MessageQueueDispatcher（透過訊息佇列非同步轉發）
/// </summary>
public class InProcessDispatcher : IExecutionDispatcher
{
    private readonly ILogger<InProcessDispatcher> _logger;

    // Phase 1 沙箱根目錄（所有檔案操作限制在此路徑下）
    private readonly string _sandboxRoot;

    public InProcessDispatcher(ILogger<InProcessDispatcher> logger, string? sandboxRoot = null)
    {
        _logger = logger;
        _sandboxRoot = sandboxRoot ?? Path.GetFullPath(".");
    }

    /// <inheritdoc />
    public Task<ExecutionResult> DispatchAsync(ApprovedRequest request)
    {
        try
        {
            var result = request.Route switch
            {
                "read_file" => ExecuteReadFile(request),
                "list_directory" => ExecuteListDirectory(request),
                "search_files" => ExecuteSearchFiles(request),
                "search_content" => ExecuteSearchContent(request),
                _ => ExecutionResult.Fail(request.RequestId,
                    $"Route '{request.Route}' not supported in Phase 1 InProcessDispatcher.")
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InProcessDispatcher failed for route {Route}", request.Route);
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, ex.Message));
        }
    }

    private ExecutionResult ExecuteReadFile(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var filePath = TryGetString(args, "path", "file_path") ?? "";

        var fullPath = ResolveSandboxedPath(filePath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!File.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"File not found: {filePath}");

        var content = File.ReadAllText(fullPath);

        // 截斷過長內容（Phase 1 限制 100KB）
        if (content.Length > 102400)
            content = content[..102400] + "\n... [truncated]";

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { path = filePath, content, size = new FileInfo(fullPath).Length }));
    }

    private ExecutionResult ExecuteListDirectory(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var dirPath = TryGetString(args, "path", "directory") ?? ".";

        var fullPath = ResolveSandboxedPath(dirPath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!Directory.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"Directory not found: {dirPath}");

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

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { path = dirPath, entries }));
    }

    private ExecutionResult ExecuteSearchFiles(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var pattern = TryGetString(args, "pattern") ?? "*";
        var basePath = TryGetString(args, "directory", "path") ?? ".";

        var fullPath = ResolveSandboxedPath(basePath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!Directory.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"Directory not found: {basePath}");

        var files = Directory.GetFiles(fullPath, pattern, SearchOption.AllDirectories)
            .Take(100)
            .Select(f => Path.GetRelativePath(fullPath, f).Replace('\\', '/'))
            .ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { basePath, pattern, matches = files }));
    }

    private ExecutionResult ExecuteSearchContent(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "pattern", "query") ?? "";
        var basePath = TryGetString(args, "directory", "path") ?? ".";
        var filePattern = TryGetString(args, "file_pattern") ?? "*";

        var fullPath = ResolveSandboxedPath(basePath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!Directory.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"Directory not found: {basePath}");

        var results = new List<object>();
        var files = Directory.GetFiles(fullPath, filePattern, SearchOption.AllDirectories).Take(500);

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
            catch (OutOfMemoryException) { throw; } // L-9 修復：不可恢復的例外必須重新拋出
            catch (Exception ex)
            {
                // 跳過無法讀取的檔案（IO 錯誤、權限不足等可恢復例外）
                _logger.LogDebug(ex, "Skipping unreadable file during content search: {File}", file);
            }
        }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, basePath, matches = results }));
    }

    /// <summary>此分派器是否支援指定路由（供 FallbackDispatcher 判斷降級）</summary>
    public bool CanHandle(string route) => route switch
    {
        "read_file" or "list_directory" or "search_files" or "search_content" => true,
        _ => false
    };

    private static JsonElement GetArgsElement(JsonElement root)
    {
        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            return args;
        if (root.TryGetProperty("tool_args", out var legacyArgs) && legacyArgs.ValueKind == JsonValueKind.Object)
            return legacyArgs;
        return root;
    }

    private static bool IsPayloadRouteValid(JsonElement root, string approvedRoute)
    {
        var payloadRoute = TryGetString(root, "route", "tool_name");
        return string.IsNullOrWhiteSpace(payloadRoute) ||
               payloadRoute.Equals(approvedRoute, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    /// <summary>解析路徑，確保在沙箱範圍內</summary>
    private string? ResolveSandboxedPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_sandboxRoot, path));

            // 確保結果路徑在沙箱根目錄下
            if (!fullPath.StartsWith(_sandboxRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
