namespace Broker.Services;

/// <summary>
/// 鑑識 agent — 每小時自動產一份過去 1 小時的 broker 事件鏈鑑識報告。
///
/// Bricks4Agent 平台第一個用 agent_inbox 抽象的 agent。所有邏輯來自 ScheduledForensicsAgentBase、
/// 這個檔案只決定身分跟節奏。
///
/// 對 Benson 的價值：原本沒被用起來的 agent abstraction 有真實任務、agent 本身的 LLM 呼叫
/// 也走 broker LlmProxy、完整對齊 broker-as-control-plane。
/// </summary>
public class ForensicsAgentService : ScheduledForensicsAgentBase
{
    public const string AgentIdConst = "agent_forensics_hourly";

    protected override string AgentId => AgentIdConst;
    protected override string PrincipalId => "prn_agent_forensics_hourly";
    protected override string DisplayName => "Forensics Investigator (hourly)";
    protected override int AutoPushIntervalSeconds => 3600;
    protected override TimeSpan DefaultWindow => TimeSpan.FromHours(1);
    protected override string DefaultQuestion =>
        "請說明過去 1 小時 broker 做了什麼、有哪些 gate 觸發、是否有異常";
    protected override string TaskType => "forensics_agent_hourly";

    public ForensicsAgentService(IServiceProvider sp, ILogger<ForensicsAgentService> logger)
        : base(sp, logger) { }
}
