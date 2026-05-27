# 後台邏輯 + API 怎麼寫 — 以 Benson 的 Task 流為範例

> 給組員的上手指南。用 Benson 親手寫的「Task」流當範本(純 Benson 程式碼、最乾淨),
> 照著抄就能做出「網頁能打的 API + 寫進 DB 的後台邏輯」。

---

## 0. 先搞清楚:系統裡有好幾種 log,別搞混

| 你看到的東西 | 是什麼 | 存在哪 | 怎麼撈 |
|---|---|---|---|
| **LINE 對話紀錄**(日期 / 回覆類型 / 錯誤訊息 / id) | 使用者跟 bot 的聊天 | `broker.db` 的 `shared_context_entries` 表(**每個 user 一列**,訊息是 JSON 陣列) | `GET /dev/conversations`、`GET /dev/conversations/{userId}`;line-admin 頁面在用 |
| **治理 / 稽核 log** | 平台每個動作:誰呼叫、成功/失敗(hash chain 防竄改) | `broker.db` 的 `audit_events` 表(**一列一筆**) | `POST /api/v1/audit/query`、`/trace`、`/verify` |

- 想看「使用者聊了什麼」→ 用 **LINE 對話紀錄**。
- 想看「平台做了什麼操作、結果如何」、要好查的表格式 log → 用 **`audit_events`**。

LINE 對話那張是「一個 user 一列、訊息塞 JSON 陣列」,不是一列一則,要逐則看得自己拆 JSON。

---

## 1. 標準四層架構

Benson 的每個功能都是這四層,各一個檔案。由下往上:

```
Model(定義 DB 表) → Service(寫 DB 的後台邏輯) → Endpoints(開 API 網址) → Program.cs(掛上去)
```

以 **Task** 為例,四個檔案:

### 第 1 層|資料模型 — 這筆東西長怎樣
**檔案**:`packages/csharp/broker-core/Models/BrokerTask.cs`

用 BaseOrm 的 attribute 對應 SQLite 表(輕量 ORM,不是 EF Core):

```csharp
[Table("broker_tasks")]          // 對應的資料表名
public class BrokerTask
{
    [Key]
    [Column("task_id")]
    public string TaskId { get; set; } = "";

    [Column("task_type")]
    public string TaskType { get; set; } = "";

    [Column("state")]
    public TaskState State { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
    // ... 其他欄位
}
```

→ 要做自己的新功能,先抄這個 class、改表名跟欄位。

### 第 2 層|後台邏輯(Service)— 真正做事 + 寫 DB
**檔案**:`packages/csharp/broker-core/Services/BrokerService.cs`
**方法**:`CreateTask()`(約第 64 行)、`GetTask()`(約第 107 行)

```csharp
public BrokerTask CreateTask(string submittedBy, string taskType, ...)
{
    var task = new BrokerTask
    {
        TaskId   = IdGen.New("task"),     // 產生 id
        TaskType = taskType,
        State    = TaskState.Created,
        CreatedAt = DateTime.UtcNow,
    };

    _db.Insert(task);                     // ← 寫進 DB(一行搞定)

    _auditService.RecordEvent(            // ← 順手記一筆稽核 log(選用)
        traceId: task.TaskId,
        eventType: "TASK_CREATED",
        principalId: submittedBy,
        taskId: task.TaskId,
        details: JsonSerializer.Serialize(new { taskType }));

    return task;
}

public BrokerTask? GetTask(string taskId) => _db.Get<BrokerTask>(taskId);   // 讀回來
```

**重點**:後台邏輯都集中在 Service,不要寫進 endpoint。
常用的 BaseOrm 方法:`_db.Insert(obj)`、`_db.Get<T>(id)`、`_db.Query<T>("SELECT ...", new { ... })`、`_db.Execute("UPDATE ...", new { ... })`。

### 第 3 層|API — 把 Service 開成網頁能打的網址
**檔案**:`packages/csharp/broker/Endpoints/TaskEndpoints.cs`(整個檔才 86 行,Benson 寫的)

```csharp
public static class TaskEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var tasks = group.MapGroup("/tasks");          // 路由前綴

        tasks.MapPost("/create", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);                  // 讀 request body
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);    // 拿登入身分
            if (!RequestBodyHelper.TryGetRequired(body, "task_type", out var taskType, out var err))
                return err!;                                            // 缺欄位 → 回錯誤

            var task = broker.CreateTask(principalId, taskType, ...);   // 呼叫第 2 層
            return Results.Ok(ApiResponseHelper.Success(task));         // 統一回 {success, data}
        });

        tasks.MapPost("/query", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out var err))
                return err!;
            var task = broker.GetTask(taskId);
            return task == null
                ? Results.NotFound(ApiResponseHelper.Error("Task not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(task));
        });
    }
}
```

- 寫入操作用 `MapPost`;要帶 body 的查詢也用 POST。
- 回傳一律包 `ApiResponseHelper.Success(...)` → JSON `{ success:true, data:... }`,前端好處理。

### 第 4 層|掛上去 — 讓 API 真的生效
**檔案**:`packages/csharp/broker/Program.cs`(約第 1094 行)

```csharp
var api = app.MapGroup("/api/v1");   // 約第 796 行,所有 API 的前綴
// ...
TaskEndpoints.Map(api);              // ← 加這一行,路由才會註冊
```

→ 最終網址:`/api/v1/tasks/create`、`/api/v1/tasks/query`。

---

## 2. 自己做一個新功能的步驟

對照上面四層:

1. **建 model**:抄 `BrokerTask.cs`,改表名 `[Table("你的表")]` + 欄位。
2. **寫 Service 方法**:在 Service 裡寫方法,內部 `_db.Insert(...)` / `_db.Get<T>(...)` / `_db.Query<T>(...)`。
3. **開 endpoint**:新建 `XxxEndpoints.cs`,照 `TaskEndpoints.cs` 的 `MapGroup` + `MapPost/MapGet`。
4. **掛載**:去 `Program.cs` 加一行 `XxxEndpoints.Map(api);`(就在 `TaskEndpoints.Map(api);` 旁邊)。

---

## 3. 怎麼測試 / 前端怎麼接

### 前端 fetch(寫在自己的網頁)

```js
// 寫入
await fetch('/api/v1/tasks/create', {
  method: 'POST',
  credentials: 'include',                          // 帶 cookie 過登入
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ task_type: 'demo' }),
});

// 讀取
const r = await fetch('/api/v1/tasks/query', {
  method: 'POST',
  credentials: 'include',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ task_id: 'task_xxx' }),
});
const j = await r.json();   // { success, data }
```

### curl 快速測

```bash
curl -X POST http://localhost:5100/api/v1/tasks/create \
  -H "Content-Type: application/json" \
  -d '{"task_type":"demo"}'
```

> **登入提醒**:這些 endpoint 會吃登入身分(`GetPrincipalId`),前端記得帶 `credentials:'include'`。
> curl 測試若被擋,先用 dashboard 登入拿 cookie,或先找沒套 auth 的 `/dev/*` endpoint 練手。

### 直接看 DB(最快 debug)

dashboard 的 **Data Browser** 分頁可以直接打 SQL:

```sql
SELECT * FROM broker_tasks ORDER BY created_at DESC LIMIT 20;
SELECT * FROM audit_events ORDER BY occurred_at DESC LIMIT 20;
```

---

## 4. 一句話總結

> `Model(定義表) → Service(寫 DB 的邏輯) → Endpoints(開 API 網址) → Program.cs(掛上去)`,
> Benson 的 Task 流就是四層各一個檔案的最小範本,照抄即可。
