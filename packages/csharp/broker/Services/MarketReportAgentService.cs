namespace Broker.Services;

/// <summary>
/// 6 小時市場報告 agent — 用較長時間窗、寫一份營運總結。
///
/// 跟 ForensicsAgent 同一條 broker-LLM pipeline，但角色不同：
///   - 鑑識（hourly）：找事件鏈、找異常、回答「發生了什麼」
///   - 報告（6h）：總結量化指標、回答「整體跑得怎樣」
/// </summary>
public class MarketReportAgentService : ScheduledForensicsAgentBase
{
    public const string AgentIdConst = "agent_market_report_6h";

    protected override string AgentId => AgentIdConst;
    protected override string PrincipalId => "prn_agent_market_report_6h";
    protected override string DisplayName => "Market Report (6-hourly)";
    protected override int AutoPushIntervalSeconds => 6 * 3600;
    protected override TimeSpan DefaultWindow => TimeSpan.FromHours(6);
    protected override string DefaultQuestion =>
        "請以營運報告風格總結過去 6 小時 broker 的活動：" +
        "1) 主要 trace 活動類型分佈、" +
        "2) approval 流量（多少 pending、approved、rejected）、" +
        "3) 哪幾種 gate 最常觸發、" +
        "4) LLM 呼叫有無異常。" +
        "用 markdown 表格 + bullet list、結尾給一句評語「整體穩定 / 留意 / 警示」";
    protected override string TaskType => "market_report_agent";

    public MarketReportAgentService(IServiceProvider sp, ILogger<MarketReportAgentService> logger)
        : base(sp, logger) { }
}
