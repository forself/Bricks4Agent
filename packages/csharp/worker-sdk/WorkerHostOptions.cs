namespace WorkerSdk;

/// <summary>
/// Worker Host 配置
/// </summary>
public class WorkerHostOptions
{
    /// <summary>Broker 主機位址</summary>
    public string BrokerHost { get; set; } = "localhost";

    /// <summary>Broker 功能池端口</summary>
    public int BrokerPort { get; set; } = 7000;

    /// <summary>Worker ID（唯一識別）</summary>
    public string WorkerId { get; set; } = $"wkr_{Guid.NewGuid():N}"[..16];

    /// <summary>最大並發任務數</summary>
    public int MaxConcurrent { get; set; } = 4;

    /// <summary>心跳間隔（秒）</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 5;

    /// <summary>連線超時（秒）</summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>重連間隔（秒）</summary>
    public int ReconnectIntervalSeconds { get; set; } = 5;

    /// <summary>是否自動重連</summary>
    public bool AutoReconnect { get; set; } = true;
}
