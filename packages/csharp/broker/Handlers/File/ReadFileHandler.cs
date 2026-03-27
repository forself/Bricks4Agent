using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.File;

public sealed class ReadFileHandler : IRouteHandler
{
    private readonly ILogger<ReadFileHandler> _logger;
    private readonly string _sandboxRoot;

    public string Route => "read_file";

    public ReadFileHandler(ILogger<ReadFileHandler> logger, string sandboxRoot)
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
        var filePath = PayloadHelper.TryGetString(args, "path", "file_path") ?? "";

        var fullPath = PayloadHelper.ResolveSandboxedPath(_sandboxRoot, filePath);
        if (fullPath == null)
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "Path outside sandbox."));

        if (!System.IO.File.Exists(fullPath))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, $"File not found: {filePath}"));

        var content = System.IO.File.ReadAllText(fullPath);

        // 截斷過長內容（Phase 1 限制 100KB）
        if (content.Length > 102400)
            content = content[..102400] + "\n... [truncated]";

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { path = filePath, content, size = new FileInfo(fullPath).Length })));
    }
}
