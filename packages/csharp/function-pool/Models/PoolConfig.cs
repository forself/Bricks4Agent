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

    /// <summary>
    /// 個別能力的分派超時（能力 id → 時間，設定 <c>FunctionPool:CapabilityDispatchTimeoutSeconds:{能力 id}</c>）。
    /// worker 處理時間較長的能力（例如生成）要設得不小於 worker 自己的逾時，否則 broker 先放棄等待並重送同一個請求。
    /// </summary>
    public Dictionary<string, TimeSpan> CapabilityDispatchTimeouts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 以設定值（能力 id → 秒數字串）加入個別能力的分派超時；不是正整數的值略過（沿用 <see cref="DispatchTimeout"/>）。
    /// </summary>
    public void AddCapabilityDispatchTimeouts(IEnumerable<KeyValuePair<string, string?>> secondsByCapability)
    {
        foreach (var (capabilityId, value) in secondsByCapability)
        {
            if (!string.IsNullOrWhiteSpace(capabilityId) &&
                int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                CapabilityDispatchTimeouts[capabilityId] = TimeSpan.FromSeconds(seconds);
            }
        }
    }

    /// <summary>這個能力的分派超時：有個別設定時用它，否則用 <see cref="DispatchTimeout"/>。</summary>
    public TimeSpan ResolveDispatchTimeout(string capabilityId)
        => !string.IsNullOrEmpty(capabilityId) &&
           CapabilityDispatchTimeouts.TryGetValue(capabilityId, out var timeout) &&
           timeout > TimeSpan.Zero
            ? timeout
            : DispatchTimeout;

    /// <summary>分派重試次數</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Worker 心跳超時時間（超過此時間未收到 PING → 標記 Disconnected）</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>健康檢查掃描間隔</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>最大 Worker 連線數</summary>
    public int MaxWorkers { get; set; } = 100;
}
