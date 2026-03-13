using CacheServer.Cluster;
using CacheServer.Engine;
using CacheServer.PubSub;
using CacheServer.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 分散式快取伺服器 — 進入點
///
/// 啟動流程：
/// 1. 讀取配置（appsettings.json / 命令列 / 環境變數）
/// 2. 初始化 BaseCache 引擎
/// 3. 建立 CacheEngine（KV + CAS + Lock）
/// 4. 建立 CommandRouter（OpCode 路由）
/// 5. （叢集模式）初始化複製、選舉、心跳元件
/// 6. 啟動 TcpCacheServer（TCP listener）
/// 7. 等待 Ctrl+C 優雅關閉
///
/// 用法：
///   dotnet run                              # 預設 port 6380
///   dotnet run -- --port 6381               # 指定 port
///   dotnet run -- --node-id node-2          # 指定 node ID
///   CacheServer__Port=6381 dotnet run       # 環境變數
/// </summary>

var builder = Host.CreateApplicationBuilder(args);

// ── 讀取配置 ──

var config = builder.Configuration;
var nodeId = config["CacheServer:NodeId"] ?? "node-1";
var port = config.GetValue("CacheServer:Port", 6380);
var bindAddress = config["CacheServer:BindAddress"] ?? "0.0.0.0";
var cleanupSeconds = config.GetValue("CacheServer:CleanupIntervalSeconds", 60);
var persistPath = config["CacheServer:PersistencePath"] ?? "";

// 叢集配置
var clusterEnabled = config.GetValue("Cluster:Enabled", false);
var clusterPriority = config.GetValue("Cluster:Priority", 1);
var advertiseHost = config["Cluster:AdvertiseHost"] ?? "localhost";
var peerList = config.GetSection("Cluster:Peers").Get<List<string>>() ?? new List<string>();

// 命令列覆蓋
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;
        case "--node-id" when i + 1 < args.Length:
            nodeId = args[++i];
            break;
        case "--bind" when i + 1 < args.Length:
            bindAddress = args[++i];
            break;
        case "--cluster":
            clusterEnabled = true;
            break;
        case "--priority" when i + 1 < args.Length:
            clusterPriority = int.Parse(args[++i]);
            break;
        case "--peer" when i + 1 < args.Length:
            peerList.Add(args[++i]);
            break;
        case "--advertise" when i + 1 < args.Length:
            advertiseHost = args[++i];
            break;
    }
}

// ── DI 註冊 ──

// BaseCache 實例（記憶體快取引擎）
var baseCacheOptions = new BaseCache.CachOptions
{
    CleanupInterval = TimeSpan.FromSeconds(cleanupSeconds)
};
var baseCache = new BaseCache.BaseCache(baseCacheOptions);

// 載入持久化資料（如有）
if (!string.IsNullOrEmpty(persistPath) && File.Exists(persistPath))
{
    try
    {
        baseCache.LoadFromFile(persistPath);
        Console.WriteLine($"  Loaded persisted data from {persistPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Warning: Failed to load persistence: {ex.Message}");
    }
}

builder.Services.AddSingleton<BaseCache.IBaseCache>(baseCache);
builder.Services.AddSingleton<CacheEngine>();
builder.Services.AddSingleton<CommandRouter>();
builder.Services.AddSingleton(sp =>
{
    var router = sp.GetRequiredService<CommandRouter>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new TcpCacheServer(router, port, bindAddress, loggerFactory);
});

// ── 叢集元件 DI 註冊 ──

var clusterConfig = new ClusterConfig
{
    NodeId = nodeId,
    Port = port,
    AdvertiseHost = advertiseHost,
    Priority = clusterPriority,
    ClusterEnabled = clusterEnabled,
    Peers = peerList
};

builder.Services.AddSingleton(clusterConfig);
builder.Services.AddSingleton<ReplicationLog>();

if (clusterEnabled)
{
    builder.Services.AddSingleton<HealthMonitor>();
    builder.Services.AddSingleton<LeaderElection>();
    builder.Services.AddSingleton<ReplicationSender>();
    builder.Services.AddSingleton<ReplicationReceiver>();
    builder.Services.AddSingleton<SnapshotTransfer>();
    builder.Services.AddSingleton<ClusterPubSub>();
}

// ── 建置 Host ──

var host = builder.Build();

// ── 啟動 TCP 伺服器 ──

var tcpServer = host.Services.GetRequiredService<TcpCacheServer>();
var commandRouter = host.Services.GetRequiredService<CommandRouter>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CacheServer");

// ── 叢集初始化 ──

HealthMonitor? healthMonitor = null;
LeaderElection? leaderElection = null;
ReplicationSender? replicationSender = null;
ReplicationReceiver? replicationReceiver = null;
SnapshotTransfer? snapshotTransfer = null;
ClusterPubSub? clusterPubSub = null;

if (clusterEnabled)
{
    healthMonitor = host.Services.GetRequiredService<HealthMonitor>();
    leaderElection = host.Services.GetRequiredService<LeaderElection>();
    replicationSender = host.Services.GetRequiredService<ReplicationSender>();
    replicationReceiver = host.Services.GetRequiredService<ReplicationReceiver>();
    snapshotTransfer = host.Services.GetRequiredService<SnapshotTransfer>();
    clusterPubSub = host.Services.GetRequiredService<ClusterPubSub>();

    // 注入叢集元件到 CommandRouter
    commandRouter.SetClusterComponents(
        replicationSender,
        replicationReceiver,
        leaderElection,
        healthMonitor,
        snapshotTransfer,
        clusterPubSub);

    // 訂閱選舉事件
    leaderElection.OnBecomeLeader += (id, p) =>
    {
        commandRouter.BecomeLeader();
        logger.LogInformation("Election result: this node ({NodeId}) is LEADER", id);
    };

    leaderElection.OnBecomeFollower += (id, leaderHost, leaderPort) =>
    {
        commandRouter.BecomeFollower(leaderHost, leaderPort);
        logger.LogInformation(
            "Election result: this node ({NodeId}) is FOLLOWER (leader={Host}:{Port})",
            id, leaderHost, leaderPort);
    };

    // 初始化選舉（單節點 → Leader；叢集 → Follower 等待選舉）
    leaderElection.Initialize();

    // 啟動健康監控
    healthMonitor.Start();
}

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Distributed Cache Server");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine($"  Node ID:    {nodeId}");
Console.WriteLine($"  Bind:       {bindAddress}:{port}");
Console.WriteLine($"  Cleanup:    {cleanupSeconds}s");
Console.WriteLine($"  Persist:    {(string.IsNullOrEmpty(persistPath) ? "(disabled)" : persistPath)}");
Console.WriteLine($"  Cluster:    {(clusterEnabled ? $"ENABLED (priority={clusterPriority}, peers={peerList.Count})" : "disabled")}");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();

var cts = new CancellationTokenSource();

// Ctrl+C 處理
Console.CancelKeyPress += async (_, e) =>
{
    e.Cancel = true;
    logger.LogInformation("Shutting down...");

    // 停止叢集元件
    if (clusterEnabled)
    {
        // Leader 優雅卸任：主動通知 Follower 開始選舉
        if (leaderElection?.Role == NodeRole.Leader)
        {
            try
            {
                logger.LogInformation("Leader stepping down before shutdown...");
                await leaderElection.StepDownAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Leader step-down failed (followers will detect timeout)");
            }
        }

        healthMonitor?.Stop();
        replicationSender?.Dispose();
        clusterPubSub?.Dispose();
        healthMonitor?.Dispose();
    }

    // 持久化（如配置）
    if (!string.IsNullOrEmpty(persistPath))
    {
        try
        {
            baseCache.SaveToFile(persistPath);
            logger.LogInformation("Persisted data to {Path}", persistPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist data");
        }
    }

    await tcpServer.StopAsync();
    cts.Cancel();
};

await tcpServer.StartAsync(cts.Token);

logger.LogInformation("Cache server started. Press Ctrl+C to stop.");

// 定期輸出狀態
_ = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), cts.Token);
            var engine = host.Services.GetRequiredService<CacheEngine>();
            var replLog = host.Services.GetRequiredService<ReplicationLog>();

            if (clusterEnabled)
            {
                var role = leaderElection?.Role.ToString() ?? "Unknown";
                logger.LogInformation(
                    "Status: role={Role}, keys={Keys}, sessions={Sessions}, " +
                    "lsn={Lsn}, casLocks={CasLocks}, distLocks={DistLocks}",
                    role,
                    engine.Count,
                    tcpServer.ActiveSessionCount,
                    replLog.CurrentLsn,
                    engine.CasLockCount,
                    engine.DistributedLockCount);
            }
            else
            {
                logger.LogInformation(
                    "Status: keys={Keys}, sessions={Sessions}, casLocks={CasLocks}, distLocks={DistLocks}",
                    engine.Count,
                    tcpServer.ActiveSessionCount,
                    engine.CasLockCount,
                    engine.DistributedLockCount);
            }
        }
        catch (OperationCanceledException) { break; }
    }
});

// 等待終止
try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // 正常退出
}

// 清理
baseCache.Dispose();
await tcpServer.DisposeAsync();

Console.WriteLine("Cache server exited.");
