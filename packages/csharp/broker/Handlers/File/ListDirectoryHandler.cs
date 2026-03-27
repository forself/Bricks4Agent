using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.File;

public sealed class ListDirectoryHandler : IRouteHandler
{
    private readonly ILogger<ListDirectoryHandler> _logger;
    private readonly string _sandboxRoot;

    public string Route => "list_directory";

    public ListDirectoryHandler(ILogger<ListDirectoryHandler> logger, string sandboxRoot)
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
        var dirPath = PayloadHelper.TryGetString(args, "path", "directory") ?? ".";

        var fullPath = PayloadHelper.ResolveSandboxedPath(_sandboxRoot, dirPath);
        if (fullPath == null)
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "Path outside sandbox."));

        if (!Directory.Exists(fullPath))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, $"Directory not found: {dirPath}"));

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

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { path = dirPath, entries })));
    }
}
