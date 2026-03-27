using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Delivery;

public sealed class DeliveryGoogleDriveShareHandler : IRouteHandler
{
    public string Route => "delivery_google_drive_share";

    private readonly ILogger<DeliveryGoogleDriveShareHandler> _logger;
    private readonly GoogleDriveShareService? _googleDriveShareService;

    public DeliveryGoogleDriveShareHandler(
        ILogger<DeliveryGoogleDriveShareHandler> logger,
        GoogleDriveShareService? googleDriveShareService = null)
    {
        _logger = logger;
        _googleDriveShareService = googleDriveShareService;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        if (_googleDriveShareService == null)
            return ExecutionResult.Fail(request.RequestId, "GoogleDriveShareService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        if (!PayloadHelper.IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var filePath = PayloadHelper.TryGetString(args, "file_path", "path") ?? string.Empty;
        var fileName = PayloadHelper.TryGetString(args, "file_name", "name") ?? string.Empty;
        var folderId = PayloadHelper.TryGetString(args, "folder_id") ?? string.Empty;
        var shareMode = PayloadHelper.TryGetString(args, "share_mode") ?? string.Empty;

        var result = await _googleDriveShareService.ShareFileAsync(
            new GoogleDriveShareRequest
            {
                FilePath = filePath,
                FileName = fileName,
                FolderId = folderId,
                ShareMode = shareMode
            });

        return result.Success
            ? ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(result))
            : ExecutionResult.Fail(request.RequestId, result.Message);
    }
}
