namespace Broker.Services;

/// <summary>
/// 每週摘要 agent — 每 7 天自動跑一次過去 7 天的事件鏈總結。
///
/// 跟 forensics_hourly / market_report_6h 的差別：時間窗最長、看週級別 trend。
/// 共用 ScheduledForensicsAgentBase 邏輯、只覆寫常數。
/// </summary>
public class WeeklyDigestAgentService : ScheduledForensicsAgentBase
{
    public const string AgentIdConst = "agent_weekly_digest";

    protected override string AgentId => AgentIdConst;
    protected override string PrincipalId => "prn_agent_weekly_digest";
    protected override string DisplayName => "Weekly Digest (7-day)";
    protected override int AutoPushIntervalSeconds => 7 * 24 * 3600;
    protected override TimeSpan DefaultWindow => TimeSpan.FromDays(7);
    protected override string DefaultQuestion =>
        "請以週報形式總結過去 7 天 broker 的活動：" +
        "1) 主要 trace 數量分佈、" +
        "2) approval 流量（pending/approved/rejected 比例）、" +
        "3) 哪幾種 gate 最常被觸發、" +
        "4) LLM 呼叫量 + 失敗率、" +
        "5) 跟前一週比有沒有顯著變化（如有資料推斷）。" +
        "用 markdown 表格 + 結尾「本週評語」一句話。";
    protected override string TaskType => "weekly_digest_agent";

    public WeeklyDigestAgentService(IServiceProvider sp, ILogger<WeeklyDigestAgentService> logger)
        : base(sp, logger) { }
}
