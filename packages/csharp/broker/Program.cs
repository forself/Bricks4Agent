using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Services;
using Broker.Adapters;
using Broker.Endpoints;
using Broker.Middleware;
using FunctionPool.Dispatch;
using FunctionPool.Health;
using FunctionPool.Models;
using FunctionPool.Network;
using FunctionPool.Registry;

var builder = WebApplication.CreateBuilder(args);

// ── 資料庫 ──
var dbPath = builder.Configuration.GetValue<string>("Database:Path") ?? "broker.db";
var connectionString = $"Data Source={dbPath}";
builder.Services.AddSingleton(sp => BrokerDb.UseSqlite(connectionString));

// ── 初始化資料庫（16 張表 + 種子資料） ──
using (var initDb = BrokerDb.UseSqlite(connectionString))
{
    var initializer = new BrokerDbInitializer(initDb);
    initializer.Initialize();
}

// ── Step 2: 加密基礎建設 ──
// ECDH P-256 金鑰對（Singleton，所有 instance 共享同一金鑰）
var ecdhPrivateKey = builder.Configuration.GetValue<string>("Broker:Encryption:EcdhPrivateKeyBase64");
if (!string.IsNullOrEmpty(ecdhPrivateKey) && !ecdhPrivateKey.StartsWith("CHANGE_ME"))
{
    builder.Services.AddSingleton<IEnvelopeCrypto>(sp =>
        new EnvelopeCrypto(ecdhPrivateKey));
}
else
{
    // 開發模式：自動生成金鑰對（每次重啟不同）
    var crypto = new EnvelopeCrypto();
    builder.Services.AddSingleton<IEnvelopeCrypto>(crypto);
    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Program");
    logger.LogWarning(
        "Using auto-generated ECDH key pair. Set Broker:Encryption:EcdhPrivateKeyBase64 in production.");
    logger.LogInformation("Broker public key (share with clients): {PublicKey}", crypto.GetBrokerPublicKey());
}

// ── Phase 2: 分散式快取（條件式接入） ──
var cacheEnabled = builder.Configuration.GetValue<bool>("CacheCluster:Enabled", false);
CacheClient.IDistributedCache? distributedCache = null;

if (cacheEnabled)
{
    var cacheNodes = builder.Configuration.GetSection("CacheCluster:Nodes").Get<List<string>>()
        ?? new List<string> { "localhost:6380" };
    var cacheOptions = new CacheClient.CacheClientOptions
    {
        Nodes = cacheNodes,
        PoolSize = builder.Configuration.GetValue("CacheCluster:PoolSize", 4),
        ConnectTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("CacheCluster:ConnectTimeoutSeconds", 5)),
        OperationTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("CacheCluster:OperationTimeoutSeconds", 3))
    };

    var rawCache = new CacheClient.DistributedCacheClient(cacheOptions);
    var fallbackCache = new CacheClient.FallbackDecorator(rawCache);
    fallbackCache.OnFallback += (operation, ex) =>
    {
        var log = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("CacheFallback");
        log.LogWarning(ex, "Cache fallback triggered: {Operation}", operation);
    };

    distributedCache = fallbackCache;
    builder.Services.AddSingleton<CacheClient.IDistributedCache>(fallbackCache);

    var log2 = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Program");
    log2.LogInformation("Cache cluster enabled: nodes={Nodes}", string.Join(", ", cacheNodes));
}

// Session 金鑰存儲（DB 後端，主金鑰加密）
var masterKeyBase64 = builder.Configuration.GetValue<string>("Broker:Encryption:MasterKeyBase64") ?? "";
var dbSessionKeyStore = new Func<IServiceProvider, DbSessionKeyStore>(sp =>
    new DbSessionKeyStore(sp.GetRequiredService<BrokerDb>(), masterKeyBase64));

if (cacheEnabled && distributedCache != null)
{
    builder.Services.AddSingleton<ISessionKeyStore>(sp =>
        new CacheSessionKeyStore(distributedCache, dbSessionKeyStore(sp)));
}
else
{
    builder.Services.AddSingleton<ISessionKeyStore>(sp => dbSessionKeyStore(sp));
}

// ── Step 3: Token + Session + Epoch ──
var tokenSecret = builder.Configuration.GetValue<string>("Broker:ScopedToken:Secret") ?? "";
var tokenIssuer = builder.Configuration.GetValue<string>("Broker:ScopedToken:Issuer") ?? "broker-control-plane";
var tokenAudience = builder.Configuration.GetValue<string>("Broker:ScopedToken:Audience") ?? "broker-agents";
var tokenExpMin = builder.Configuration.GetValue<int>("Broker:ScopedToken:ExpirationMinutes");
if (tokenExpMin <= 0) tokenExpMin = 15;

builder.Services.AddSingleton<IScopedTokenService>(sp =>
    new ScopedTokenService(tokenSecret, tokenIssuer, tokenAudience, tokenExpMin));

builder.Services.AddSingleton<ISessionService>(sp =>
    new SessionService(sp.GetRequiredService<BrokerDb>()));

if (cacheEnabled && distributedCache != null)
{
    builder.Services.AddSingleton<IRevocationService>(sp =>
        new CacheRevocationService(sp.GetRequiredService<BrokerDb>(), distributedCache));
}
else
{
    builder.Services.AddSingleton<IRevocationService>(sp =>
        new RevocationService(sp.GetRequiredService<BrokerDb>()));
}

// ── Step 4: Audit Trail ──
builder.Services.AddSingleton<IAuditService>(sp =>
    new AuditService(sp.GetRequiredService<BrokerDb>()));

// ── Step 5: Capability Catalog + Policy Engine ──
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddSingleton<ITaskRouter, TaskRouter>();

if (cacheEnabled && distributedCache != null)
{
    builder.Services.AddSingleton<ICapabilityCatalog>(sp =>
        new CacheCapabilityCatalog(
            sp.GetRequiredService<BrokerDb>(),
            distributedCache,
            new CapabilityCatalog(sp.GetRequiredService<BrokerDb>())));
}
else
{
    builder.Services.AddSingleton<ICapabilityCatalog>(sp =>
        new CapabilityCatalog(sp.GetRequiredService<BrokerDb>()));
}

builder.Services.AddSingleton<IPolicyEngine>(sp =>
    new PolicyEngine(sp.GetRequiredService<ISchemaValidator>()));

// ── Step 6 + 7: BrokerService + ExecutionDispatcher ──
// Phase 3: 功能池（條件式啟用）
var poolEnabled = builder.Configuration.GetValue<bool>("FunctionPool:Enabled", false);
var strictMode = builder.Configuration.GetValue<bool>("FunctionPool:StrictMode", false);

if (poolEnabled)
{
    var poolConfig = new PoolConfig
    {
        ListenPort = builder.Configuration.GetValue("FunctionPool:ListenPort", 7000),
        BindAddress = builder.Configuration.GetValue("FunctionPool:BindAddress", "0.0.0.0") ?? "0.0.0.0",
        DispatchTimeout = TimeSpan.FromSeconds(
            builder.Configuration.GetValue("FunctionPool:DispatchTimeoutSeconds", 30)),
        MaxRetries = builder.Configuration.GetValue("FunctionPool:MaxRetries", 2),
        HeartbeatTimeout = TimeSpan.FromSeconds(
            builder.Configuration.GetValue("FunctionPool:HeartbeatTimeoutSeconds", 30)),
        HealthCheckInterval = TimeSpan.FromSeconds(
            builder.Configuration.GetValue("FunctionPool:HealthCheckIntervalSeconds", 10)),
        MaxWorkers = builder.Configuration.GetValue("FunctionPool:MaxWorkers", 100)
    };

    builder.Services.AddSingleton(poolConfig);
    builder.Services.AddSingleton<IWorkerRegistry, WorkerRegistry>();
    builder.Services.AddSingleton<PoolListener>();
    builder.Services.AddSingleton(sp =>
        new WorkerHealthMonitor(
            sp.GetRequiredService<IWorkerRegistry>(),
            sp.GetRequiredService<PoolConfig>(),
            sp.GetRequiredService<ILogger<WorkerHealthMonitor>>(),
            sp.GetService<IObservationService>()));

    if (strictMode)
    {
        // 生產模式：StrictPoolDispatcher — 無 Worker 就 fail，絕不降級
        builder.Services.AddSingleton<IExecutionDispatcher>(sp =>
        {
            var poolDispatcher = new PoolDispatcher(
                sp.GetRequiredService<IWorkerRegistry>(),
                poolConfig,
                sp.GetRequiredService<ILogger<PoolDispatcher>>());
            return new StrictPoolDispatcher(
                poolDispatcher,
                sp.GetRequiredService<ILogger<StrictPoolDispatcher>>());
        });
    }
    else
    {
        // 開發/測試模式：FallbackDispatcher — Pool + InProcess 降級
        builder.Services.AddSingleton<IExecutionDispatcher>(sp =>
        {
            var inProcess = new InProcessDispatcher(
                sp.GetRequiredService<ILogger<InProcessDispatcher>>());
            var poolDispatcher = new PoolDispatcher(
                sp.GetRequiredService<IWorkerRegistry>(),
                poolConfig,
                sp.GetRequiredService<ILogger<PoolDispatcher>>());
            return new FallbackDispatcher(
                poolDispatcher, inProcess, inProcess.CanHandle,
                sp.GetRequiredService<ILogger<FallbackDispatcher>>());
        });
    }

    var poolLog = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Program");
    poolLog.LogInformation(
        "Function pool enabled: port={Port}, strictMode={Strict}",
        poolConfig.ListenPort, strictMode);
}
else
{
    builder.Services.AddSingleton<IExecutionDispatcher>(sp =>
        new InProcessDispatcher(sp.GetRequiredService<ILogger<InProcessDispatcher>>()));
}

// ── Phase 4: 因果工作流服務 ──
builder.Services.AddSingleton<ISharedContextService>(sp =>
    new SharedContextService(
        sp.GetRequiredService<BrokerDb>(),
        sp.GetRequiredService<IAuditService>()));

builder.Services.AddSingleton<IPlanService>(sp =>
    new PlanService(
        sp.GetRequiredService<BrokerDb>(),
        sp.GetRequiredService<IAuditService>()));

builder.Services.AddSingleton<IObservationService>(sp =>
    new ObservationService(
        sp.GetRequiredService<BrokerDb>(),
        sp.GetRequiredService<IAuditService>()));

builder.Services.AddSingleton<IBrokerService>(sp =>
    new BrokerService(
        sp.GetRequiredService<BrokerDb>(),
        sp.GetRequiredService<IPolicyEngine>(),
        sp.GetRequiredService<IAuditService>(),
        sp.GetRequiredService<ICapabilityCatalog>(),
        sp.GetRequiredService<ISessionService>(),
        sp.GetRequiredService<IRevocationService>(),
        sp.GetRequiredService<ITaskRouter>(),
        sp.GetRequiredService<IExecutionDispatcher>()));

builder.Services.AddSingleton<IPlanEngine>(sp =>
    new PlanEngine(
        sp.GetRequiredService<IPlanService>(),
        sp.GetRequiredService<IBrokerService>(),
        sp.GetRequiredService<ISharedContextService>(),
        sp.GetRequiredService<IAuditService>(),
        sp.GetRequiredService<IObservationService>()));

var app = builder.Build();

// ── Middleware 管線（順序關鍵） ──
// [1] ExceptionMiddleware（全域例外）— TODO: Phase 2
// [2] IpRateLimiter（限流）— TODO: Phase 2
// [3] EncryptionMiddleware（信封解密/加密）
app.UseEnvelopeEncryption();
// [4] BrokerAuthMiddleware（Token 驗證）
app.UseBrokerAuth();
// [5] AuditMiddleware（稽核記錄）
app.UseBrokerAudit();

// ── 路由（全 POST，零 GET） ──
var api = app.MapGroup("/api/v1");

// 健康檢查（唯一非加密端點，用於 load balancer）
api.MapPost("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

// 業務端點
TaskEndpoints.Map(api);
SessionEndpoints.Map(api);
ExecutionEndpoints.Map(api);
CapabilityEndpoints.Map(api);
AuditEndpoints.Map(api);
AdminEndpoints.Map(api);
ContextEndpoints.Map(api);
PlanEndpoints.Map(api);

// ── Phase 3: 啟動功能池 TCP Listener ──
if (poolEnabled)
{
    var poolListener = app.Services.GetRequiredService<PoolListener>();
    await poolListener.StartAsync();

    var healthMonitor = app.Services.GetRequiredService<WorkerHealthMonitor>();
    healthMonitor.Start();

    // 優雅關閉
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() =>
    {
        healthMonitor.Dispose();
        poolListener.StopAsync().GetAwaiter().GetResult();
    });
}

app.Run();
