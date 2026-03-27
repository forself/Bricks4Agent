using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.File;

public sealed class SearchFilesHandler : IRouteHandler
{
    private readonly ILogger<SearchFilesHandler> _logger;
    private readonly string _sandboxRoot;

    public string Route => "search_files";

    public SearchFilesHandler(ILogger<SearchFilesHandler> logger, string sandboxRoot)
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
        var pattern = PayloadHelper.TryGetString(args, "pattern") ?? "*";
        var basePath = PayloadHelper.TryGetString(args, "directory", "path") ?? ".";

        var fullPath = PayloadHelper.ResolveSandboxedPath(_sandboxRoot, basePath);
        if (fullPath == null)
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "Path outside sandbox."));

        if (!Directory.Exists(fullPath))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, $"Directory not found: {basePath}"));

        var files = Directory.GetFiles(fullPath, pattern, SearchOption.AllDirectories)
            .Take(100)
            .Select(f => Path.GetRelativePath(fullPath, f).Replace('\\', '/'))
            .ToList();

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { basePath, pattern, matches = files })));
    }
}
