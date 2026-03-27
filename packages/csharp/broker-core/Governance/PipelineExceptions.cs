namespace BrokerCore.Governance;

/// <summary>
/// PEP Pipeline 例外登記 —— 記錄所有已知的 PEP bypass
///
/// 每項例外需包含：ID、描述、理由、預計移除階段。
/// 建構時 log 所有登記項目，確保 bypass 可追蹤。
///
/// 規則：
/// - 永久例外須有明確的類別理由（如「非能力調用」）
/// - 暫時例外須標記預計移除的 Phase
/// - 新增 bypass 時必須在此登記，否則 ApprovedRequest bypass 偵測會發出 warning
/// </summary>
public static class PipelineExceptions
{
    public static readonly IReadOnlyList<PipelineException> Registry = new[]
    {
        // ── 永久例外 ──

        new PipelineException(
            Id: "EXC-LINE-INBOUND-HTTP",
            Description: "LINE worker 透過 HTTP POST 將 inbound 訊息轉發至 broker /api/v1/high-level/line/process",
            Justification: "訊息收集端點在類別上不同於能力調用。Coordinator 內部觸發的能力調用走 PEP。",
            PlannedRemovalPhase: null,
            AffectedFiles: new[]
            {
                "packages/csharp/workers/line-worker/InboundDispatcher.cs"
            }),

        new PipelineException(
            Id: "EXC-DEV-ENDPOINTS",
            Description: "/dev/* 開發診斷端點",
            Justification: "非能力調用，是 admin/debug 工具。Production 必須透過 config 關閉。",
            PlannedRemovalPhase: null,
            AffectedFiles: Array.Empty<string>()),

        new PipelineException(
            Id: "EXC-SYSTEM-BOOTSTRAP",
            Description: "Broker 啟動時建立 system session 與 grants",
            Justification: "PEP 需要既有 session 才能運作，bootstrap 無法走 PEP。僅在啟動時執行一次。",
            PlannedRemovalPhase: null,
            AffectedFiles: Array.Empty<string>()),

        // ── 暫時例外（待消除） ──

        new PipelineException(
            Id: "EXC-HLQM-BYPASS",
            Description: "HighLevelQueryToolMediator 手動建構 ApprovedRequest，繞過 PEP 16 步流程",
            Justification: "歷史遺留：高階查詢直接呼叫 dispatcher。應改為走 IBrokerService.SubmitExecutionRequestAsync。",
            PlannedRemovalPhase: "Phase 2",
            AffectedFiles: new[]
            {
                "packages/csharp/broker/Services/HighLevelQueryToolMediator.cs"
            }),
    };
}

/// <summary>
/// 單一 PEP 例外項目
/// </summary>
public sealed record PipelineException(
    string Id,
    string Description,
    string Justification,
    string? PlannedRemovalPhase,
    string[] AffectedFiles);
