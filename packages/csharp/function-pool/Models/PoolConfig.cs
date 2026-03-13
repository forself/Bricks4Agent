namespace FunctionPool.Models;

/// <summary>
/// 功能池配置
/// </summary>
public class PoolConfig
{
    /// <summary>TCP 監聽端口（Worker 連入）</summary>
    public int ListenPort { get; set; } = 7000;

    /// <summary>TCP 綁定位址</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>分派超時時間</summary>
    public TimeSpan DispatchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>分派重試次數</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Worker 心跳超時時間（超過此時間未收到 PING → 標記 Disconnected）</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>健康檢查掃描間隔</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>最大 Worker 連線數</summary>
    public int MaxWorkers { get; set; } = 100;
}
