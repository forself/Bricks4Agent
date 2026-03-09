# BaseCache

輕量級記憶體快取，類似 Redis 的精簡版。零外部依賴，純 .NET 實作。

## 特點

- **零依賴** - 不需要安裝任何 NuGet 套件
- **執行緒安全** - 使用 `ConcurrentDictionary` 實作
- **多種資料結構** - Key-Value、Queue、Stack、List、Hash、Set
- **TTL 支援** - 自動過期機制
- **Pub/Sub** - 發布訂閱模式
- **持久化** - 可儲存/載入 JSON 檔案
- **統計資訊** - 命中率、讀寫次數等

## 安裝

直接引用專案或複製 `BaseCache.cs` 到你的專案中。

## 快速開始

```csharp
using BaseCache;

var cache = new BaseCache();

// 基本 Key-Value
cache.Set("user:1", new User { Name = "John" }, TimeSpan.FromMinutes(30));
var user = cache.Get<User>("user:1");

// 取得或設定
var data = cache.GetOrSet("expensive:query", () => {
    return LoadFromDatabase();
}, TimeSpan.FromHours(1));
```

## API 參考

### Key-Value 操作

```csharp
// 設定 (可選 TTL)
cache.Set("key", value);
cache.Set("key", value, TimeSpan.FromMinutes(30));

// 取得
var value = cache.Get<T>("key");

// 嘗試取得
if (cache.TryGet<T>("key", out var value)) { }

// 取得或設定
var value = cache.GetOrSet("key", () => factory(), ttl);

// 刪除
cache.Delete("key");

// 檢查存在
bool exists = cache.Exists("key");

// 設定過期時間
cache.Expire("key", TimeSpan.FromMinutes(10));

// 取得剩餘時間
var ttl = cache.TTL("key");

// 搜尋 keys (支援 * 和 ? 萬用字元)
var keys = cache.Keys("user:*");

// 數值操作
cache.Increment("counter");
cache.Increment("counter", 5);
cache.Decrement("counter");
```

### Queue 佇列 (FIFO)

```csharp
cache.Enqueue("jobs", new Job { Id = 1 });
cache.Enqueue("jobs", new Job { Id = 2 });

var job = cache.Dequeue<Job>("jobs");  // Job { Id = 1 }
var peek = cache.QueuePeek<Job>("jobs");  // 查看但不移除
var length = cache.QueueLength("jobs");
```

### Stack 堆疊 (LIFO)

```csharp
cache.Push("history", "page1");
cache.Push("history", "page2");

var page = cache.Pop<string>("history");  // "page2"
var peek = cache.StackPeek<string>("history");
var length = cache.StackLength("history");
```

### List 列表

```csharp
cache.RPush("list", "a");  // 右側加入
cache.RPush("list", "b");
cache.LPush("list", "z");  // 左側加入

var items = cache.LRange<string>("list", 0, -1);  // ["z", "a", "b"]
var first = cache.LPop<string>("list");  // "z"
var last = cache.RPop<string>("list");   // "b"
var item = cache.LIndex<string>("list", 0);  // 依索引取得
var length = cache.LLen("list");
```

### Hash 雜湊表

```csharp
cache.HSet("user:1", "name", "John");
cache.HSet("user:1", "email", "john@example.com");

var name = cache.HGet<string>("user:1", "name");
var all = cache.HGetAll<string>("user:1");  // Dictionary
var keys = cache.HKeys("user:1");  // ["name", "email"]
var exists = cache.HExists("user:1", "name");
var length = cache.HLen("user:1");

cache.HDel("user:1", "email");
cache.HIncr("user:1", "visits");  // 數值遞增
```

### Set 集合

```csharp
cache.SAdd("tags", "csharp");
cache.SAdd("tags", "dotnet");

var members = cache.SMembers("tags");  // HashSet
var isMember = cache.SIsMember("tags", "csharp");  // true
var count = cache.SCard("tags");  // 2

cache.SRemove("tags", "dotnet");

// 集合運算
var intersection = cache.SIntersect("set1", "set2");
var union = cache.SUnion("set1", "set2");
```

### Pub/Sub 發布訂閱

```csharp
// 訂閱
cache.Subscribe("notifications", (channel, message) => {
    Console.WriteLine($"[{channel}] {message}");
});

// 發布
int subscribers = cache.Publish("notifications", "New order received!");

// 取消訂閱
cache.Unsubscribe("notifications");
```

### 持久化

```csharp
// 儲存到檔案
cache.SaveToFile("cache_backup.json");

// 從檔案載入
cache.LoadFromFile("cache_backup.json");
```

### 批次操作

```csharp
// 批次設定
cache.SetMany(new Dictionary<string, User> {
    ["user:1"] = user1,
    ["user:2"] = user2
}, TimeSpan.FromHours(1));

// 批次取得
var users = cache.GetMany<User>(new[] { "user:1", "user:2" });

// 批次刪除
cache.DeleteMany(new[] { "key1", "key2" });

// 依模式刪除
cache.DeleteByPattern("user:*");
```

### 統計資訊

```csharp
Console.WriteLine(cache.Stats);
// Hits: 150, Misses: 10, HitRate: 93.75%, Reads: 160, Writes: 50, Evictions: 5

Console.WriteLine($"命中率: {cache.Stats.HitRate:F2}%");
```

### 配置選項

```csharp
var cache = new BaseCache(new CachOptions
{
    CleanupInterval = TimeSpan.FromMinutes(5),  // 清理間隔
    MaxItems = 10000,  // 最大項目數 (0 = 無限制)
    EvictionCount = 100  // 達上限時移除的項目數
});
```

## 使用場景

### 1. API 響應快取

```csharp
public async Task<UserDto> GetUserAsync(int id)
{
    return cache.GetOrSet($"user:{id}", async () => {
        return await _repository.GetUserAsync(id);
    }, TimeSpan.FromMinutes(10));
}
```

### 2. 速率限制

```csharp
public bool IsRateLimited(string clientIp)
{
    var key = $"rate:{clientIp}";
    var count = cache.Increment(key);

    if (count == 1)
        cache.Expire(key, TimeSpan.FromMinutes(1));

    return count > 100;  // 每分鐘 100 次
}
```

### 3. Session 儲存

```csharp
public void SetSession(string sessionId, SessionData data)
{
    cache.HSet($"session:{sessionId}", "user_id", data.UserId);
    cache.HSet($"session:{sessionId}", "created_at", DateTime.UtcNow);
    cache.Expire($"session:{sessionId}", TimeSpan.FromHours(24));
}
```

### 4. 任務佇列

```csharp
// 生產者
cache.Enqueue("email:queue", new EmailJob { To = "user@example.com" });

// 消費者
while (true)
{
    var job = cache.Dequeue<EmailJob>("email:queue");
    if (job != null)
        await SendEmailAsync(job);
    else
        await Task.Delay(1000);
}
```

### 5. 即時通知

```csharp
// 訂閱用戶通知
cache.Subscribe($"user:{userId}:notifications", (_, msg) => {
    SendWebSocket(userId, msg);
});

// 發送通知
cache.Publish($"user:{userId}:notifications", new {
    type = "message",
    content = "You have a new message"
});
```

## 注意事項

1. **記憶體限制** - 這是純記憶體實作，重啟後資料會遺失 (除非使用持久化)
2. **單機使用** - 不支援分散式，如需分散式請使用 Redis
3. **大型物件** - 避免儲存過大的物件，建議單一項目 < 1MB
4. **序列化** - 持久化功能使用 System.Text.Json，複雜物件需確保可序列化

## License

MIT
