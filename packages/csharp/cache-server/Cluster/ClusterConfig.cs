namespace CacheServer.Cluster;

/// <summary>
/// 叢集配置
///
/// 描述叢集拓撲：哪些節點存在、各自的角色/優先權。
/// 從 appsettings.json 或環境變數載入。
/// </summary>
public class ClusterConfig
{
    /// <summary>本節點 ID（唯一識別）</summary>
    public string NodeId { get; set; } = "node-1";

    /// <summary>本節點監聽端口</summary>
    public int Port { get; set; } = 6380;

    /// <summary>本節點對外 host（其他節點連線用）</summary>
    public string AdvertiseHost { get; set; } = "localhost";

    /// <summary>選舉優先權（越小越優先，0 = 最高優先）</summary>
    public int Priority { get; set; } = 1;

    /// <summary>是否啟用叢集模式</summary>
    public bool ClusterEnabled { get; set; }

    /// <summary>
    /// Peer 節點列表
    /// 格式：nodeId:host:port:priority
    /// 例如："node-2:192.168.1.2:6381:2"
    /// </summary>
    public List<string> Peers { get; set; } = new();

    /// <summary>解析 peer 列表為結構化資料</summary>
    public List<PeerInfo> GetPeers()
    {
        var result = new List<PeerInfo>();
        foreach (var peer in Peers)
        {
            var parts = peer.Split(':');
            if (parts.Length >= 3)
            {
                result.Add(new PeerInfo
                {
                    NodeId = parts[0],
                    Host = parts[1],
                    Port = int.Parse(parts[2]),
                    Priority = parts.Length >= 4 ? int.Parse(parts[3]) : 10
                });
            }
        }
        return result;
    }

    /// <summary>叢集總節點數（含自身）</summary>
    public int TotalNodes => 1 + Peers.Count;

    /// <summary>Quorum 大小（N/2 + 1，含自身）</summary>
    public int QuorumSize => (TotalNodes / 2) + 1;
}

/// <summary>Peer 節點資訊</summary>
public class PeerInfo
{
    public string NodeId { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public int Priority { get; set; }
}

/// <summary>節點角色</summary>
public enum NodeRole
{
    /// <summary>追隨者（只處理讀取，寫入 REDIRECT 到 Leader）</summary>
    Follower = 0,

    /// <summary>領導者（處理所有操作，負責複製）</summary>
    Leader = 1,

    /// <summary>候選人（選舉進行中）</summary>
    Candidate = 2
}
