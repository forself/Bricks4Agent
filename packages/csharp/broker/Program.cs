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

// ── 共用 LoggerFactory（修復 M-7：消除冗餘 LoggerFactory 實例） ──
using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("Program");

// ── 資料庫 ──
var dbPath = builder.Configuration.GetValue<string>("Database:Path") ?? "broker.db";
var connectionString = $"Data Source={dbPath}";
builder.Services.AddSingleton(sp => BrokerDb.UseSqlite(connectionString));

// ── 初始化資料庫（17 張表 + 種子資料） ──
using (var initDb = BrokerDb.UseSqlite(connectionString))
{
    var initializer = new BrokerDbInitializer(initDb);
    var developmentSeed = builder.Configuration.GetSection("DevelopmentSeed").Get<DevelopmentSeedOptions>();
    initializer.Initialize(developmentSeed);
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
    // M-3 修復：不再將完整金鑰記錄到 log（即使是 public key 也不應洩漏至 log aggregation）
    startupLogger.LogWarning(
        "Using auto-generated ECDH key pair. Set Broker:Encryption:EcdhPrivateKeyBase64 in production.");
    var pubKey = crypto.GetBrokerPublicKey();
    startupLogger.LogInformation(
        "Broker public key generated (length={KeyLength}, prefix={KeyPrefix}...)",
        pubKey.Length, pubKey[..Math.Min(8, pubKey.Length)]);
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
    var cacheFallbackLogger = startupLoggerFactory.CreateLogger("CacheFallback");
    fallbackCache.OnFallback += (operation, ex) =>
    {
        cacheFallbackLogger.LogWarning(ex, "Cache fallback triggered: {Operation}", operation);
    };

    distributedCache = fallbackCache;
    builder.Services.AddSingleton<CacheClient.IDistributedCache>(fallbackCache);
    startupLogger.LogInformation("Cache cluster enabled: nodes={Nodes}", string.Join(", ", cacheNodes));
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

// M-2 修復：PolicyEngine 黑名單從配置讀取（無配置時使用預設值）
var policyOptions = builder.Configuration.GetSection("PolicyEngine").Get<PolicyEngineOptions>()
    ?? new PolicyEngineOptions();
builder.Services.AddSingleton<IPolicyEngine>(sp =>
    new PolicyEngine(sp.GetRequiredService<ISchemaValidator>(), policyOptions));
var llmProxyOptions = builder.Configuration.GetSection("LlmProxy").Get<LlmProxyOptions>()
    ?? new LlmProxyOptions();
builder.Services.AddSingleton(llmProxyOptions);
builder.Services.AddHttpClient<ILlmProxyService, LlmProxyService>();

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

    startupLogger.LogInformation(
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
// [0] BodySizeLimitMiddleware（H-10 修復：防止 DoS 超大 payload）
var maxBodyBytes = builder.Configuration.GetValue<long>("Broker:MaxRequestBodyBytes", 1_048_576); // 1MB default
app.UseBodySizeLimit(maxBodyBytes);
// [1] ExceptionMiddleware（全域例外）— TODO: Phase 5
// [2] IpRateLimiter（限流）— TODO: Phase 5
// [3] EncryptionMiddleware（信封解密/加密）
app.UseEnvelopeEncryption();
// [4] BrokerAuthMiddleware（Token 驗證）
app.UseBrokerAuth();
// [5] AuditMiddleware（稽核記錄）
app.UseBrokerAudit();

// ── 路由 ──
var api = app.MapGroup("/api/v1");

// L-7 修復：健康檢查同時支援 GET（標準 LB 探測）和 POST（向後相容）
var healthHandler = () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow });
api.MapGet("/health", healthHandler);
api.MapPost("/health", healthHandler);

// 業務端點
TaskEndpoints.Map(api);
SessionEndpoints.Map(api);
ExecutionEndpoints.Map(api);
CapabilityEndpoints.Map(api);
AuditEndpoints.Map(api);
AdminEndpoints.Map(api);
ContextEndpoints.Map(api);
PlanEndpoints.Map(api);
RuntimeEndpoints.Map(api);

// ── Phase 3: 啟動功能池 TCP Listener ──
if (poolEnabled)
{
    var poolListener = app.Services.GetRequiredService<PoolListener>();
    await poolListener.StartAsync();

    var healthMonitor = app.Services.GetRequiredService<WorkerHealthMonitor>();
    healthMonitor.Start();

    // 優雅關閉
    // L-8 修復：Register 只接受 Action（非 Func<Task>），sync-over-async 在 shutdown hook 不可避免
    // 加入 10 秒 timeout 防止 shutdown 卡住（無 SynchronizationContext，不會 deadlock）
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() =>
    {
        healthMonitor.Dispose();
        if (!poolListener.StopAsync().Wait(TimeSpan.FromSeconds(10)))
        {
            // StopAsync 超時，強制繼續 shutdown 流程
        }
    });
}

app.Run();
