# BaseLogger Governance Logging Design

更新日期：2026-06-20

## 1. 背景

Bricks4Agent 是 broker-centered governed AI operations platform。LOG / Audit / Observability 不是一般維運附屬功能，而是治理、追溯、事故復盤、可信證據鏈與狀態外部化的核心。

目前專案已存在 `packages/csharp/logging/BaseLogger`，且 broker 已 reference 此專案；但主線 runtime logging 仍主要使用 `Microsoft.Extensions.Logging.ILogger<T>` 與 console provider。`BaseLogger` 目前比較像未正式接入的平台元件，並且公開型別如 `ILogger`、`Logger`、`LogLevel`、`LogEntry`、`ILogTarget` 容易與 .NET logging abstraction 或常見基礎函式庫命名混淆。

本設計保留 `BaseLogger` 作為套件、專案與 namespace 名稱，但將其升級為 Bricks4Agent 的治理式 logging pipeline 元件，並避免在 `BaseLogger` namespace 底下暴露與系統或基本函式庫 logging 型別重名的主要公開 API。

## 2. 設計目標

1. `BaseLogger` 成為正式、泛用、可抽換的 logging component。
2. 應用層仍依賴 `Microsoft.Extensions.Logging.ILogger<T>`，不直接依賴 `BaseLogger` 具體實作。
3. `BaseLogger` 透過 `Microsoft.Extensions.Logging` provider 接入 broker、rag-service、workers。
4. `BaseLogger` 補足標準 logging abstraction 不負責的治理能力：correlation、redaction、structured event、rolling/retention、memory live view、database sink、security rejection audit bridge。
5. runtime log、audit event、observation、RAG retrieval、LLM call metadata 可以用同一個 trace/correlation model 串接。
6. `AuditService` 仍是治理事件與 hash-chain audit trail 的 canonical store，不與 runtime log 混為同一機制。
7. 前置拒絕事件，例如 body too large、rate limited、decryption failed、missing/invalid token，也要能進入 minimal audit trail。

## 3. 非目標

1. 不用 `BaseLogger` 平替 `Microsoft.Extensions.Logging`。
2. 不要求全庫把 constructor 從 `ILogger<T>` 改成 BaseLogger 型別。
3. 不把一般 runtime log 全部寫成 audit event。
4. 不把 raw interaction log、memory、execution payload、audit event 合併成同一資料表。
5. 不在第一期引入外部 SIEM、OpenTelemetry collector 或分散式 trace backend；但 sink 介面需保留擴充空間。

## 4. 命名原則

`BaseLogger` namespace 保留，但主要公開型別避免使用過度泛用或易撞名名稱。

應避免作為新主要 API 的名稱：

- `ILogger`
- `Logger`
- `Log`
- `LogLevel`
- `LogEntry`
- `ILogTarget`

新的主要 API 採明確前綴與責任命名：

| 責任 | 建議型別 |
| --- | --- |
| 單筆事件 | `BaseLoggerEvent` |
| 嚴重度 | `BaseLoggerSeverity` |
| pipeline | `BaseLoggerPipeline` |
| 設定 | `BaseLoggerOptions` |
| sink 介面 | `IBaseLoggerSink` |
| formatter | `IBaseLoggerFormatter` |
| redactor | `IBaseLoggerRedactor` |
| correlation accessor | `IBaseLoggerCorrelationAccessor` |
| ASP.NET / worker provider | `BaseLoggerProvider` |
| provider extension | `BaseLoggerLoggingBuilderExtensions` |
| rolling file sink | `BaseLoggerRollingFileSink` |
| console sink | `BaseLoggerConsoleSink` |
| database sink | `BaseLoggerDatabaseSink` |
| memory sink | `BaseLoggerMemorySink` |

舊型別可以短期保留為 compatibility wrapper，但需標記 `[Obsolete]`，並在 README 與手冊中改用新型別。

## 5. 架構概覽

```text
Application code
  -> Microsoft.Extensions.Logging.ILogger<T>
  -> BaseLoggerProvider
  -> BaseLoggerPipeline
  -> BaseLoggerCorrelationAccessor
  -> BaseLoggerRedactor
  -> BaseLoggerFormatter
  -> sinks
       - console
       - rolling file
       - database operational log
       - memory live view
       - future OTLP / SIEM sink

Security rejection path
  -> security middleware
  -> BaseLogger runtime event
  -> SecurityRejectionAuditRecorder
  -> BrokerCore.AuditService
  -> audit_events hash chain
```

`BaseLoggerProvider` 是應用層的 logging provider。它接受 `Microsoft.Extensions.Logging` 事件，轉換成 `BaseLoggerEvent`，交給 `BaseLoggerPipeline`。

`BaseLoggerPipeline` 負責處理：

- scope/context merge
- severity mapping
- property normalization
- redaction
- formatting
- sink dispatch
- sink failure isolation

## 6. Event Schema

`BaseLoggerEvent` 應包含以下欄位：

| 欄位 | 說明 |
| --- | --- |
| `event_id` | pipeline 產生的唯一事件 ID |
| `timestamp_utc` | UTC 時間 |
| `severity` | `BaseLoggerSeverity` |
| `category` | .NET logger category |
| `event_kind` | `runtime`, `security_rejection`, `integration`, `worker`, `rag`, `llm`, `audit_bridge` 等 |
| `message_template` | 原始 message template |
| `rendered_message` | render 後且 redacted 的訊息 |
| `exception` | redacted exception string |
| `properties` | redacted structured properties |
| `trace_id` | broker-wide trace id |
| `span_id` | optional local operation span id |
| `principal_id` | redacted/hash 後的 principal id 或明確允許的 system principal |
| `task_id` | task correlation |
| `session_id` | session correlation |
| `request_id` | HTTP/request correlation |
| `component` | broker、line-worker、rag-service、browser-worker 等 |
| `process_id` | process id |
| `thread_id` | managed thread id |
| `source` | host/service/source name |
| `sensitivity` | `public`, `internal`, `restricted`, `secret` |

`BaseLoggerSeverity` 與 `Microsoft.Extensions.Logging.LogLevel` 的映射：

| Microsoft | BaseLogger |
| --- | --- |
| `Trace` | `Trace` |
| `Debug` | `Debug` |
| `Information` | `Info` |
| `Warning` | `Warning` |
| `Error` | `Error` |
| `Critical` | `Critical` |
| `None` | `None` |

## 7. Provider 接入

host setup 採 composition-root 抽換：

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddBaseLogger(builder.Configuration);
```

應用程式碼仍使用：

```csharp
Microsoft.Extensions.Logging.ILogger<T>
```

這代表 broker services、middleware、workers、rag-service 不需要因 logging component 接入而大規模改 constructor。若某些路徑需要特殊治理資訊，應透過 scope 或 correlation accessor 設定，而不是把 BaseLogger 具體型別傳入業務服務。

## 8. Correlation Model

新增 early correlation middleware，位置需早於 body-size limit、rate limit、encryption、worker auth、broker auth、audit middleware。

責任：

1. 建立或讀取 `broker_trace_id`。
2. 寫入 `HttpContext.Items`。
3. 建立 `ILogger.BeginScope`，讓所有 runtime log 自動帶入 correlation property。
4. 將 trace id 放入 error response metadata，避免使用者只能看到 500 而無法回報追蹤碼。
5. 供 `AuditMiddleware`、`ExecutionEndpoints`、`PlanEndpoints`、security rejection audit recorder 重用同一 trace id。

建議 `HttpContext.Items` keys：

```text
broker_trace_id
broker_trace_source
broker_request_id
broker_principal_id
broker_task_id
broker_session_id
broker_component
```

## 9. Redaction Policy

`IBaseLoggerRedactor` 是必備元件，不是 optional helper。

預設策略：

1. 屬性名稱包含以下詞時一律遮罩：`token`, `secret`, `key`, `password`, `authorization`, `cookie`, `signature`, `credential`。
2. `user_id`, `principal_id`, `session_id`, IP address 預設以 stable hash 表示；若是 system principal 可保留明文。
3. LINE message、assistant reply、upstream response body 預設截斷且標為 restricted。
4. exception string 應移除明顯 token/header/body secret。
5. audit details 與 runtime log 可採不同 redaction profile，但都不可存 raw secret。

redaction 必須在所有 sink 之前完成，sink 不應自行決定是否遮罩。

## 10. Sink Model

`IBaseLoggerSink` 介面負責接收已 redacted 的 `BaseLoggerEvent`。

第一期 sink：

1. `BaseLoggerConsoleSink`
   - 本機開發與容器 stdout。
   - 支援 plain text 與 JSON。

2. `BaseLoggerRollingFileSink`
   - 支援 max size、max files、retention days。
   - sidecar 使用 rolling file sink，避免每次啟動覆蓋上一輪 evidence。

3. `BaseLoggerDatabaseSink`
   - 儲存 operational logs，不取代 `audit_events`。
   - 表名需固定或白名單設定，不接受任意 table name。

4. `BaseLoggerMemorySink`
   - 供 local admin / diagnostics 顯示近期 log。
   - ring buffer 限制容量，避免長期佔用記憶體。

後續 sink：

- OTLP / OpenTelemetry exporter
- SIEM sink
- compressed archive sink

sink 失敗不可造成主流程失敗；pipeline 需記錄 sink health，但避免 recursive logging storm。

## 11. Audit 邊界

`AuditService` 保持 canonical audit trail。

runtime log 和 audit event 的差異：

| 類型 | 用途 | 儲存 |
| --- | --- | --- |
| runtime log | 排錯、營運觀測、性能與錯誤上下文 | BaseLogger sinks |
| audit event | 治理決策、權限、approval、execution、security rejection、可信追溯 | `audit_events` |
| raw interaction log | 使用者輸入/輸出原始真相 | `SharedContextEntry` document family |
| observation | 狀態/計畫/worker 觀測事件 | `observation_events` + audit bridge |

security rejection audit bridge 只針對治理相關拒絕事件寫入 audit，例如：

- body too large
- rate limit exceeded
- invalid encrypted envelope
- replay detected
- missing token
- invalid token
- revoked token/session
- unauthorized worker identity

details 採 minimal schema：

```json
{
  "method": "POST",
  "path": "/api/v1/example",
  "status_code": 401,
  "reason_code": "missing_token",
  "client_hash": "sha256:...",
  "component": "broker"
}
```

不得記錄 token、decrypted body、完整 IP、完整 user content。

## 12. Configuration

新增 `BaseLogger` 設定區：

```json
{
  "BaseLogger": {
    "Enabled": true,
    "MinimumSeverity": "Info",
    "Format": "json",
    "Component": "broker",
    "RedactionProfile": "production",
    "Sinks": {
      "Console": {
        "Enabled": true
      },
      "RollingFile": {
        "Enabled": true,
        "Path": ".run/logs/broker.jsonl",
        "MaxFileSizeBytes": 10485760,
        "MaxFiles": 10,
        "RetentionDays": 14
      },
      "Database": {
        "Enabled": false,
        "TableName": "operational_logs"
      },
      "Memory": {
        "Enabled": true,
        "MaxEntries": 500
      }
    }
  }
}
```

worker 與 rag-service 可使用同一 schema，只改 `Component` 與 log path。

## 13. Compatibility Strategy

現有 `BaseLogger` 型別不可一次破壞。

策略：

1. 新 API 先建立。
2. 舊 API 轉接新 pipeline。
3. 舊型別標 `[Obsolete]`，訊息需指向替代型別。
4. README、manual、broker setup 改用新 API。
5. 單元測試同時覆蓋新 API 與 compatibility wrapper。
6. 後續版本再決定是否移除舊 API。

## 14. Implementation Phases

### Phase 1：BaseLogger API 整理

- 建立 `BaseLoggerEvent`、`BaseLoggerSeverity`、`BaseLoggerPipeline`、`IBaseLoggerSink`、`IBaseLoggerFormatter`、`IBaseLoggerRedactor`。
- 將現有 console/file/database/memory target 遷移或包裝成 sink。
- 舊 `ILogger`、`Logger`、`LogLevel`、`LogEntry`、`ILogTarget` 保留 compatibility wrapper。

### Phase 2：Microsoft.Extensions.Logging Provider

- 建立 `BaseLoggerProvider`。
- 建立 `BaseLoggerMicrosoftLogger` 或內部 adapter，避免公開命名撞 `ILogger`。
- 建立 `AddBaseLogger` extension。
- broker、workers、rag-service 只在 host setup 接入。

### Phase 3：Correlation 與 Security Rejection Audit

- 新增 early correlation middleware。
- AuditMiddleware 改用同一 `broker_trace_id`。
- ExceptionHandlingMiddleware 改用同一 trace id 並回傳 trace metadata。
- body-limit、rate-limit、encryption、worker auth、broker auth 拒絕事件寫 minimal audit。

### Phase 4：Sidecar 與 Runtime Retention

- sidecar log 不再啟動即刪除上一輪。
- 使用 run id 或 rolling file retention。
- runbook 補上 log retention、查詢與清理方式。

### Phase 5：Testing 與 Documentation

- 增加 BaseLogger unit tests。
- 增加 broker middleware correlation/security rejection tests。
- 更新 user manual、technical manual、follow-up planning。

## 15. Testing Requirements

必備測試：

1. provider 將 `ILogger<T>` 事件轉成 `BaseLoggerEvent`。
2. `BeginScope` properties 會合併到 event。
3. redactor 遮罩 token、secret、authorization、cookie。
4. rolling file sink 達到大小後 rotate，且 retention 不覆蓋最新 evidence。
5. database sink 建表與 insert 正常，table name validation 阻擋危險名稱。
6. correlation middleware 在未授權請求也會建立 trace id。
7. body too large / rate limited / invalid token 會產生 minimal audit event。
8. `/audit/trace` 與 `/audit/verify` 非 admin 不可查他人 trace。
9. exception response 包含 trace id，但不洩漏 exception details。
10. compatibility wrapper 可運作，但新文件不使用舊型別。

## 16. Acceptance Criteria

第一期完成後，應滿足：

1. `BaseLogger` namespace 保留。
2. 新主要 API 不再使用 `ILogger`、`Logger`、`LogLevel`、`LogEntry`、`ILogTarget` 作為公開主名稱。
3. broker、rag-service、workers 可透過 `builder.Logging.AddBaseLogger(...)` 接入。
4. 應用層 constructor 不需大規模從 `ILogger<T>` 改成 BaseLogger 型別。
5. runtime log、audit event、exception response 能以同一 `broker_trace_id` 串接。
6. 前置拒絕事件可查詢於 audit trail。
7. redaction policy 在所有 sink 之前執行。
8. sidecar log 不再無條件覆蓋上一輪 evidence。
9. 文件明確說明 runtime log、audit event、raw interaction log、observation 的邊界。

## 17. Explicit Implementation Defaults

本設計已決定保留 `BaseLogger` 名稱與 namespace。第一期 implementation plan 採以下預設，不再留待實作者猜測：

1. `BaseLoggerDatabaseSink` 第一批先完成 library-level verify；broker SQLite operational log 接入排在 provider、redaction、correlation 測試穩定之後。
2. `BaseLoggerMemorySink` 第一批只提供內部 ring buffer 查詢 API；local admin diagnostics endpoint 另列後續 UI/API 工作。
3. sidecar runtime log retention 預設保留 14 天、每個 stream 最多 10 個 rolling files。
4. audit HMAC / external anchoring 不納入此批；本批只確保前置拒絕事件進入既有 `AuditService` hash-chain audit trail。
