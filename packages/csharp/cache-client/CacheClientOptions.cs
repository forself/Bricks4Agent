namespace CacheClient;

/// <summary>
/// 快取客戶端配置
/// </summary>
public class CacheClientOptions
{
    /// <summary>叢集節點列表 (host:port)</summary>
    public List<string> Nodes { get; set; } = new() { "localhost:6380" };

    /// <summary>每個節點的連線池大小</summary>
    public int PoolSize { get; set; } = 4;

    /// <summary>連線超時</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>操作超時</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>重試次數</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>心跳間隔</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>連線閒置超時（超過此時間回收）</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
