# BaseLogger

輕量級日誌元件，支援多種輸出目標。可與 BaseCache、BaseOrm 整合。

## 特點

- **零依賴** - 核心功能不需要任何 NuGet 套件
- **多輸出目標** - Console、File、Database、Memory Cache
- **結構化日誌** - 支援 JSON 格式
- **非同步寫入** - 不阻塞主執行緒
- **檔案輪替** - 自動依大小輪替日誌檔
- **Fluent API** - 鏈式配置
- **BaseCache 整合** - 記憶體快取 + 即時串流
- **BaseOrm 整合** - 資料庫持久化

## 快速開始

```csharp
using BaseLogger;

// 簡單使用
var logger = new Logger();
logger.Info("Application started");
logger.Error("Something went wrong", exception);

// 靜態存取
Log.Info("Hello World");
Log.Error("Failed", ex);
```

## 日誌等級

| 等級 | 說明 |
|------|------|
| Trace | 最詳細的追蹤資訊 |
| Debug | 除錯資訊 |
| Info | 一般資訊 |
| Warn | 警告 |
| Error | 錯誤 |
| Fatal | 致命錯誤 |

## 配置選項

```csharp
var logger = new Logger(new LoggerOptions
{
    MinLevel = LogLevel.Debug,        // 最低等級
    EnableConsole = true,             // Console 輸出
    EnableColors = true,              // 彩色輸出
    UseJsonFormat = false,            // JSON 格式
    AsyncFlush = true,                // 非同步刷新
    FlushInterval = TimeSpan.FromSeconds(1),
    BufferSize = 100,                 // 緩衝大小
    MaxFileSize = 10 * 1024 * 1024,   // 檔案大小上限 (10MB)
    MaxRollingFiles = 5               // 輪替檔案數
});
```

## 輸出目標

### Console 輸出

```csharp
var logger = new Logger(new LoggerOptions
{
    EnableConsole = true,
    EnableColors = true
});
```

### 檔案輸出

```csharp
var logger = new Logger()
    .AddFile("logs/app.log")
    .AddFile("logs/errors.log", LogLevel.Error);  // 只記錄錯誤
```

### 記憶體快取輸出 (BaseCache)

```csharp
using BaseCache;
using BaseLogger;

var cache = new BaseCache();
var logger = new Logger()
    .AddMemoryCache(cache, "log:", maxEntries: 1000);

// 取得最近的日誌
var target = logger.Targets.OfType<MemoryCacheLogTarget>().First();
var recentLogs = target.GetRecentLogs(100);

// 訂閱即時日誌串流
target.Subscribe(entry => {
    Console.WriteLine($"[LIVE] {entry.Message}");
});

// 取得統計
var stats = target.GetStats();
// { "Error": 5, "Warn": 10, "Info": 100, ... }
```

### 資料庫輸出 (BaseOrm)

```csharp
using BaseOrm;
using BaseLogger;

var db = new BaseDb("Data Source=app.db");
var logger = new Logger()
    .AddDatabase(db, "AppLogs");

// 會自動建立資料表：
// CREATE TABLE AppLogs (
//     Id INTEGER PRIMARY KEY,
//     Timestamp TEXT,
//     Level INTEGER,
//     Message TEXT,
//     Category TEXT,
//     Exception TEXT,
//     Properties TEXT,
//     TraceId TEXT,
//     ThreadId TEXT
// )
```

### 自訂輸出目標

```csharp
logger.AddCustomTarget(entry => {
    // 發送到外部服務
    SendToElasticSearch(entry);
}, LogLevel.Error);
```

## 結構化日誌

### 附加屬性

```csharp
logger.Info("User logged in", new Dictionary<string, object?>
{
    ["userId"] = 123,
    ["ip"] = "192.168.1.1",
    ["browser"] = "Chrome"
});
```

### 使用 Fluent API

```csharp
logger
    .ForCategory("Auth")
    .WithProperty("userId", 123)
    .WithTraceId("abc-123")
    .Info("Login successful");
```

### JSON 輸出

```csharp
var logger = new Logger(new LoggerOptions { UseJsonFormat = true });

// 輸出:
// {"Timestamp":"2026-01-25T10:30:00Z","Level":2,"Message":"User logged in","Properties":{"userId":123}}
```

## 進階用法

### 分類日誌

```csharp
var authLogger = logger.ForCategory("Auth");
var dbLogger = logger.ForCategory("Database");

authLogger.Info("User logged in");  // [Auth] User logged in
dbLogger.Debug("Query executed");   // [Database] Query executed
```

### 追蹤 ID

```csharp
// 在請求開始時設定
var requestLogger = logger.WithTraceId(Guid.NewGuid().ToString());

requestLogger.Info("Request started");
requestLogger.Info("Processing...");
requestLogger.Info("Request completed");
// 所有日誌都會包含相同的 TraceId
```

### 執行時間追蹤

```csharp
// 使用 Scope
using (logger.BeginScope("ProcessOrder"))
{
    // ... 執行操作
}
// 自動記錄: Begin: ProcessOrder
//          End: ProcessOrder (duration_ms: 150)

// 使用 LogExecution
var result = logger.LogExecution("FetchData", () => {
    return database.GetData();
});

// 非同步版本
var data = await logger.LogExecutionAsync("FetchDataAsync", async () => {
    return await database.GetDataAsync();
});
```

### 靜態存取

```csharp
// 設定預設 Logger
Log.Default = new Logger()
    .AddFile("logs/app.log")
    .AddMemoryCache(cache);

// 使用靜態方法
Log.Info("Application started");
Log.Error("Failed", exception);

// 應用程式結束時
Log.CloseAndFlush();
```

## 完整範例

```csharp
using BaseCache;
using BaseOrm;
using BaseLogger;

// 建立依賴
var cache = new BaseCache();
var db = new BaseDb("Data Source=app.db");

// 配置日誌
var logger = new Logger(new LoggerOptions
{
    MinLevel = LogLevel.Debug,
    EnableColors = true,
    AsyncFlush = true
})
.AddFile("logs/app.log")
.AddFile("logs/errors.log", LogLevel.Error)
.AddMemoryCache(cache, "log:", 1000)
.AddDatabase(db, "Logs");

// 設為預設
Log.Default = logger;

// 使用
var authLogger = Log.Default.ForCategory("Auth");

try
{
    authLogger.Info("User login attempt", new Dictionary<string, object?>
    {
        ["email"] = "user@example.com"
    });

    // ... 登入邏輯

    authLogger.Info("Login successful");
}
catch (Exception ex)
{
    authLogger.Error("Login failed", ex);
}

// 取得即時統計
var cacheTarget = logger.Targets.OfType<MemoryCacheLogTarget>().First();
var stats = cacheTarget.GetStats();
Console.WriteLine($"Errors: {stats["Error"]}, Warnings: {stats["Warn"]}");

// 應用程式結束
Log.CloseAndFlush();
```

## ASP.NET Core 整合

```csharp
// Program.cs
var logger = new Logger()
    .AddFile("logs/api.log")
    .AddMemoryCache(cache);

Log.Default = logger;

// Middleware
app.Use(async (context, next) =>
{
    var requestLogger = Log.Default
        .ForCategory("HTTP")
        .WithTraceId(context.TraceIdentifier);

    requestLogger.Info($"{context.Request.Method} {context.Request.Path}");

    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();

    requestLogger.Info($"Response {context.Response.StatusCode}", new Dictionary<string, object?>
    {
        ["duration_ms"] = sw.ElapsedMilliseconds
    });
});
```

## 注意事項

1. **非同步刷新** - 預設啟用，程式結束前請呼叫 `Flush()` 或 `Dispose()`
2. **檔案權限** - 確保應用程式有寫入日誌目錄的權限
3. **記憶體使用** - MemoryCache 會保留指定數量的日誌在記憶體中
4. **資料庫效能** - DatabaseLogTarget 使用批次寫入，每 5 秒刷新一次

## License

MIT
