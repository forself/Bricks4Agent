using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// etcd lease 選主 —— 對應已實證的 failover-sim 機制(lease 互斥選主 + 失主自我降級)。
/// 用 etcd v3 JSON gateway via HttpClient(brokersim.py 驗證過的同條路徑,免額外 NuGet 依賴)。
///
/// 階段①(唯讀):只選主 + 暴露 IsPrimary + log role 變化,**不 gate 任何行為**。
/// 先在真環境觀察選主穩不穩(網路抖動會不會誤判換主);穩了再進階段②(gate 非真錢)→ ④(gate 真錢)。
///
/// 啟用條件:設定 Cluster:EtcdEndpoints(逗號分隔)。未設 → 用 SingleNodeLeaderElection(永遠主、現狀不變)。
/// </summary>
public sealed class EtcdLeaderElection : BackgroundService, ILeaderElection
{
    private readonly string[] _endpoints;
    private readonly int _ttl;
    private readonly ILogger<EtcdLeaderElection> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private volatile NodeRole _role = NodeRole.Starting;

    private const string LeaderKey = "/b4a/broker/leader";

    public bool IsPrimary => _role == NodeRole.Primary;
    public NodeRole Role => _role;
    public string NodeId { get; }

    public EtcdLeaderElection(string[] endpoints, string nodeId, int ttlSeconds, ILogger<EtcdLeaderElection> logger)
    {
        _endpoints = endpoints;
        NodeId = nodeId;
        _ttl = ttlSeconds;
        _logger = logger;
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    /// <summary>打 etcd v3 gateway;輪流試每個 endpoint(模擬連得到 quorum 的那側)。全不可達 → null。</summary>
    private async Task<JsonElement?> Etcd(string path, object body, CancellationToken ct)
    {
        foreach (var ep in _endpoints)
        {
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync($"http://{ep}/v3/{path}", content, ct);
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                return doc.RootElement.Clone();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* try next endpoint */ }
        }
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("EtcdLeaderElection 啟動 node={Node} ttl={Ttl}s endpoints=[{Eps}](階段①唯讀、不 gate)",
            NodeId, _ttl, string.Join(",", _endpoints));
        // 等 host 起穩、避免 startup race
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1) 取 lease
                var grant = await Etcd("lease/grant", new { TTL = _ttl }, ct);
                if (grant is null) { SetRole(NodeRole.Standby, "etcd 不可達(可能被分區)→ 自我 fence"); await Delay(2000, ct); continue; }
                var leaseId = grant.Value.GetProperty("ID").GetString();

                // 2) 選主:txn 若 leader key 不存在(version==0)才搶下、綁我的 lease
                var txn = await Etcd("kv/txn", new
                {
                    compare = new[] { new { key = B64(LeaderKey), result = "EQUAL", target = "VERSION", version = "0" } },
                    success = new[] { new { requestPut = new { key = B64(LeaderKey), value = B64(NodeId), lease = leaseId } } },
                }, ct);
                bool won = txn is not null && txn.Value.TryGetProperty("succeeded", out var s) && s.GetBoolean();
                if (!won) { SetRole(NodeRole.Standby, null); await Delay(1000, ct); continue; }

                SetRole(NodeRole.Primary, "取得 leadership");

                // 3) 續租 + 確認主權(階段①只 keepalive + 偵測失主、不 gate 任何工作)
                while (!ct.IsCancellationRequested)
                {
                    var ka = await Etcd("lease/keepalive", new { ID = leaseId }, ct);
                    if (ka is null) { SetRole(NodeRole.Standby, "續租失敗/被分區 → 自我降級"); break; }

                    var get = await Etcd("kv/range", new { key = B64(LeaderKey) }, ct);
                    string? cur = null;
                    if (get is not null && get.Value.TryGetProperty("kvs", out var kvs) && kvs.GetArrayLength() > 0)
                        cur = Encoding.UTF8.GetString(Convert.FromBase64String(kvs[0].GetProperty("value").GetString()!));
                    if (cur != NodeId) { SetRole(NodeRole.Standby, "leader key 已非本節點 → 降級(避免雙主)"); break; }

                    await Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SetRole(NodeRole.Standby, $"例外 → 自我 fence:{ex.Message}");
                if (!await Delay(2000, ct)) break;
            }
        }
    }

    private static async Task<bool> Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); return true; } catch (OperationCanceledException) { return false; }
    }

    private void SetRole(NodeRole r, string? reason)
    {
        if (_role == r) return;
        _role = r;
        _logger.LogWarning("[cluster] role → {Role}{Reason}", r, reason is null ? "" : $" ({reason})");
    }
}
