namespace CacheClient;

/// <summary>
/// Leader 追蹤器
///
/// 追蹤叢集中哪個節點是 Leader：
/// - 初始時假設第一個節點是 Leader
/// - 收到 REDIRECT 時更新 Leader
/// - 定期心跳確認 Leader 是否存活
/// </summary>
public class LeaderTracker
{
    private volatile int _leaderIndex;
    private readonly List<(string Host, int Port)> _nodes;
    private readonly object _lock = new();

    public LeaderTracker(IEnumerable<string> nodeAddresses)
    {
        _nodes = new List<(string, int)>();

        foreach (var addr in nodeAddresses)
        {
            var parts = addr.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 ? int.Parse(parts[1]) : 6380;
            _nodes.Add((host, port));
        }

        if (_nodes.Count == 0)
            throw new ArgumentException("At least one node is required");

        _leaderIndex = 0;
    }

    /// <summary>當前 Leader 索引</summary>
    public int LeaderIndex => _leaderIndex;

    /// <summary>當前 Leader 位址</summary>
    public (string Host, int Port) LeaderAddress => _nodes[_leaderIndex];

    /// <summary>節點數量</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>取得指定索引的節點位址</summary>
    public (string Host, int Port) GetNodeAddress(int index) => _nodes[index % _nodes.Count];

    /// <summary>
    /// 根據 REDIRECT 回應更新 Leader
    /// </summary>
    public void UpdateLeader(string host, int port)
    {
        lock (_lock)
        {
            // 尋找匹配的節點
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Host == host && _nodes[i].Port == port)
                {
                    _leaderIndex = i;
                    return;
                }
            }

            // 未知節點：加入清單並設為 Leader
            _nodes.Add((host, port));
            _leaderIndex = _nodes.Count - 1;
        }
    }

    /// <summary>
    /// 將 Leader 切換到下一個節點（Leader 故障時）
    /// </summary>
    public void RotateLeader()
    {
        lock (_lock)
        {
            _leaderIndex = (_leaderIndex + 1) % _nodes.Count;
        }
    }

    /// <summary>
    /// 取得下一個讀取節點（Round-Robin，排除某節點）
    /// </summary>
    private int _readRobin;

    public int NextReadIndex()
    {
        return Interlocked.Increment(ref _readRobin) % _nodes.Count;
    }
}
