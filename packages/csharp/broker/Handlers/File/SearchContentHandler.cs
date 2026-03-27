using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.File;

public sealed class SearchContentHandler : IRouteHandler
{
    private readonly ILogger<SearchContentHandler> _logger;
    private readonly string _sandboxRoot;

    public string Route => "search_content";

    public SearchContentHandler(ILogger<SearchContentHandler> logger, string sandboxRoot)
    {
        _logger = logger;
        _sandboxRoot = sandboxRoot;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!PayloadHelper.IsPayloadRouteValid(doc.RootElement, request.Route))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route."));

        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "pattern", "query") ?? "";
        var basePath = PayloadHelper.TryGetString(args, "directory", "path") ?? ".";
        var filePattern = PayloadHelper.TryGetString(args, "file_pattern") ?? "*";

        var fullPath = PayloadHelper.ResolveSandboxedPath(_sandboxRoot, basePath);
        if (fullPath == null)
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "Path outside sandbox."));

        if (!Directory.Exists(fullPath))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, $"Directory not found: {basePath}"));

        var results = new List<object>();
        var files = Directory.GetFiles(fullPath, filePattern, SearchOption.AllDirectories).Take(500);

        foreach (var file in files)
        {
            try
            {
                var lines = System.IO.File.ReadAllLines(file);
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

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, basePath, matches = results })));
    }
}
