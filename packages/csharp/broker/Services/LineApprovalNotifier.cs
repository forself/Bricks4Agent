using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// IApprovalNotifier 的 LINE 實作(§18.2-C2)。
/// User 層審批建立時,組可看內容的簽章連結,經 LINE 通知佇列送給擁有者
/// (line-worker 取出佇列實際推送,沿用 artifact 交付的同一機制)。
/// </summary>
public sealed class LineApprovalNotifier : IApprovalNotifier
{
    private readonly ApprovalLinkService _link;
    private readonly SidecarPublicUrlResolver _url;
    private readonly HighLevelLineWorkspaceService _workspace;
    private readonly ILogger<LineApprovalNotifier> _logger;

    public LineApprovalNotifier(
        ApprovalLinkService link,
        SidecarPublicUrlResolver url,
        HighLevelLineWorkspaceService workspace,
        ILogger<LineApprovalNotifier> logger)
    {
        _link = link;
        _url = url;
        _workspace = workspace;
        _logger = logger;
    }

    public void NotifyUserApprovalCreated(ApprovalRequest approval)
    {
        try
        {
            var link = TryBuildLink(approval);
            if (link == null)
            {
                _logger.LogWarning(
                    "Approval {ApprovalId}: no public URL or token; skipped LINE notify.", approval.ApprovalId);
                return;
            }

            var title = "待審動作";
            var body = $"{approval.CapabilityId} 需要你確認。\n原因:{approval.Reason}\n點此查看內容並決定:\n{link}";
            _workspace.QueueLineNotification(approval.OwnerPrincipalId, title, body);
            _logger.LogInformation(
                "Queued approval link notification for {Owner} ({ApprovalId}).",
                approval.OwnerPrincipalId, approval.ApprovalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify user approval {ApprovalId}.", approval.ApprovalId);
        }
    }

    /// <summary>組可看內容的審批連結;無 token(secret 未設)或無 public URL(無 tunnel)回 null。</summary>
    public string? TryBuildLink(ApprovalRequest approval)
    {
        var token = _link.CreateToken(approval.OwnerPrincipalId);
        if (string.IsNullOrEmpty(token))
            return null;
        var baseUrl = _url.TryGetPublicBaseUrl();
        if (string.IsNullOrEmpty(baseUrl))
            return null;
        return $"{baseUrl}/user-approvals.html#token={Uri.EscapeDataString(token)}";
    }
}
