namespace BrokerCore.Contracts;

/// <summary>
/// 審批明細(§18.2-C2)—— 管理員與使用者兩面 web UI 共用的形狀。
/// 由 ApprovalRequest + 關聯 ExecutionRequest + Capability 組裝,含「內容渲染」。
/// </summary>
public class ApprovalDetail
{
    public string ApprovalId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    /// <summary>"User" 或 "Admin"</summary>
    public string Tier { get; set; } = string.Empty;
    public string OwnerPrincipalId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>內容渲染:讓審批者看了內容再決定(賣點)</summary>
    public RenderedContent Rendered { get; set; } = new();
}

/// <summary>
/// 依能力把請求 payload 渲染成可視內容。
/// kind=patch(repo.patch.apply)/command(build.test.run)/file(file.write)/json(其他)。
/// </summary>
public class RenderedContent
{
    public string Kind { get; set; } = "json";
    public string? Patch { get; set; }
    public string? Command { get; set; }
    public string? Path { get; set; }
    public string? ContentPreview { get; set; }
    public string? Payload { get; set; }
}
