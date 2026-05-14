namespace Broker.Services;

/// <summary>
/// 15 分鐘異常偵測 agent — 高頻短窗、抓「不對勁」訊號早期。
///
/// 跟另外兩個 agent 互補：
///   - 鑑識（hourly）：事後分析「發生了什麼」
///   - 報告（6h）：定期總結「整體狀態」
///   - 異常偵測（15min）：即時警示「有沒有問題正在發生」
///
/// Prompt 明確要求「若一切正常請說無異常、不要編造」、避免 LLM 為了顯得有用而幻想異常事件。
/// </summary>
public class AnomalyDetectorAgentService : ScheduledForensicsAgentBase
{
    public const string AgentIdConst = "agent_anomaly_detector_15m";

    protected override string AgentId => AgentIdConst;
    protected override string PrincipalId => "prn_agent_anomaly_detector_15m";
    protected override string DisplayName => "Anomaly Detector (15-min)";
    protected override int AutoPushIntervalSeconds => 15 * 60;
    protected override TimeSpan DefaultWindow => TimeSpan.FromMinutes(15);
    protected override string DefaultQuestion =>
        "請偵測過去 15 分鐘的異常訊號：" +
        "approval 拒絕、gate 高頻觸發（同一 gate >3 次）、LLM 失敗、健康分數驟降、dispatch 重試。" +
        "**若一切正常請明確回答「✓ 無異常」、不要編造**。" +
        "若有異常、列出每個異常的時間戳 + 影響範圍 + 建議下一步動作。";
    protected override string TaskType => "anomaly_detector_agent";

    public AnomalyDetectorAgentService(IServiceProvider sp, ILogger<AnomalyDetectorAgentService> logger)
        : base(sp, logger) { }
}
