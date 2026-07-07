# 後續規劃

更新日期：2026-06-20

本文件記錄目前已辨識、但尚未排入當前實作批次的中長期規劃項。原則是避免把可行性驗證誤寫成產品級完成，也避免後續討論散落在不同設計文件中。

## LOG / Audit / Observability 治理化

### 現況

目前系統同時存在多種紀錄機制：

- broker、rag-service、workers 主要使用 `Microsoft.Extensions.Logging` 輸出到 console。
- LINE sidecar 會把 broker、worker、tunnel stdout/stderr 導到 `.run/line-sidecar/logs/*`。
- `BrokerCore.Services.AuditService` 將 execution、approval、plan、shared context 等治理事件寫入 `audit_events`，並以 per-trace hash chain 驗證一致性。
- high-level raw interaction log 與 `convlog:*` 目前寫入 `SharedContextEntry`，它是對話真相紀錄，不等同 runtime log。
- `ObservationService` 與 `LlmProxyMetrics` 提供觀測事件與 LLM proxy metrics，但 metrics 仍以記憶體 ring buffer 為主。
- `BaseLogger` 專案存在且具 file/db/memory target、rolling 與 trace id 能力，但目前未成為 broker 主線 logging pipeline。

### 風險

LOG / Audit / Observability 不應只被視為維運便利功能。對 Bricks4Agent 這類 broker-centered governed AI operations platform 而言，它直接影響：

- 安全事件是否可追溯。
- AI agent 的行為是否能被事後重建。
- approval、execution、RAG retrieval、external tool access 是否具可信證據鏈。
- incident review 是否能區分 user input、broker decision、policy result、worker action 與 model output。

目前最重要的缺口是：

1. `AuditMiddleware` 位於 body-limit、rate-limit、encryption、worker auth、broker auth 之後；因此 413、429、解密失敗、missing/invalid token 等前置拒絕事件多數只進 runtime log，沒有進 `audit_events`。
2. audit trace id、ASP.NET `TraceIdentifier`、一般 `ILogger` scope 尚未統一，runtime log 與 audit event 難以穩定串接。
3. `/api/v1/audit/trace` 與 `/api/v1/audit/verify` 需要補齊 principal/admin 授權邊界，避免 trace id 被取得後可越權查看事件。
4. sidecar log 目前啟動時覆蓋舊檔，沒有 retention、rotation、structured JSON 或集中查詢策略。
5. LINE/user content、upstream response body、token/session/error body 的 redaction policy 尚未一致。
6. audit hash chain 目前是 DB 內一致性驗證，尚未加入 HMAC、外部錨定或 append-only sink。
7. 缺少針對 audit middleware、前置拒絕事件、trace correlation、audit endpoint authorization、hash-chain tamper 的正式測試。

### 後續方向

1. 建立 canonical correlation model。
   - 統一產生 `broker_trace_id`。
   - 注入 `HttpContext.Items`、audit event、`ILogger.BeginScope`、error response。
   - 在 execution request、plan、observation、RAG retrieval event 中持續傳遞。

2. 建立 security rejection audit。
   - 對 body-limit、rate-limit、encryption、worker auth、broker auth 的拒絕事件寫入 minimal audit。
   - 只記 path、method、status、reason code、client hash、trace id、timestamp。
   - 不記 decrypted body、token、secret、完整 IP 或完整使用者輸入。

3. 修正 audit endpoint authorization。
   - `/audit/query`、`/audit/trace`、`/audit/verify` 採一致的 admin / principal / task / session 檢查。
   - 非 admin 僅能查自己的 trace。

4. 產品化 runtime logging。
   - 決定 canonical stack：優先沿用 `Microsoft.Extensions.Logging`，接 structured JSON / file sink / OpenTelemetry。
   - `BaseLogger` 若要保留，需正式接入；否則標記為 legacy 或移除 broker reference，避免雙軌制度。
   - sidecar log 加入 rotation、retention、啟動批次識別與封存策略。

5. 建立 redaction policy。
   - LINE user id、訊息內容、reply preview、upstream body、token/session/key/error body 需一致遮罩。
   - audit details 與 runtime log 採不同敏感度等級。

6. 加強可信證據鏈。
   - audit hash chain 可加入 HMAC 或定期外部錨定。
   - RAG retrieval、LLM call metadata、tool invocation、worker evidence ref 納入可查詢 audit/observation。

7. 補正式測試。
   - 前置拒絕事件會寫 audit。
   - trace id 在 runtime log scope 與 audit event 一致。
   - 非 admin 無法查他人 trace。
   - 篡改 `audit_events` 後 verify 會失敗。
   - sidecar log rotation/retention 不會覆蓋上一輪證據。

### 優先級

高優先。LOG / Audit / Observability 是治理式 AI 系統的可信度、事故復盤、外部狀態可驗證性與安全邊界的核心，不應排在一般中期優化之後。它應與 RAG 狀態外部化、approval/execution governance 並列為平台可信基礎。

## RAG 儲存層與知識庫服務化

### 現況

目前 RAG 可用 SQLite 承載小型/中型 proof-of-concept、單機 broker、離線驗證與法律 RAG fixture。SQLite FTS5 已能支援 CJK 全文檢索，`SharedContextEntry` 可支援狀態外部化，`vector_entries` 可支援基本向量檢索驗證。

新增的 `packages/csharp/rag-service` 已讓 retrieval core 可作為獨立服務啟動，但儲存層仍以 SQLite/BaseOrm 現有資料模型為主。

### 判斷

SQLite 不應被視為完整產品級 RAG 的唯一最終承載。它適合作為：

- local development provider
- offline deterministic verification provider
- single-node deployment provider
- edge/local agent cache

但若目標是多使用者、多代理、多知識庫、自主 RAG service，SQLite 會在併發寫入、知識庫隔離、向量索引、備份/replication、線上 reindex 與水平擴展上不足。

### 後續方向

1. 增加 `KnowledgeBase` 抽象。
   - `kb_id`
   - owner / tenant / ACL
   - source manifest
   - ingestion job
   - chunk/document version
   - retrieval event audit

2. 抽出 RAG storage provider。
   - SQLite provider：保留給 dev/test/offline/single-node。
   - PostgreSQL provider：正式 metadata/state store。
   - Vector provider：先支援 pgvector；後續可接 Qdrant / Milvus / Weaviate。

3. 擴充 standalone `rag-service`。
   - `POST /knowledge-bases`
   - `POST /knowledge-bases/{kb_id}/documents`
   - `POST /knowledge-bases/{kb_id}/ingest`
   - `POST /knowledge-bases/{kb_id}/retrieve`
   - `POST /knowledge-bases/{kb_id}/reindex`

4. 保持驗證策略。
   - SQLite fixture tests 繼續保留為 deterministic gate。
   - PostgreSQL / vector store 作為 integration profile，不阻斷離線開發。

### 優先級

中優先。它不是目前法律 RAG POC 的阻斷項，但會決定 RAG 是否能從「broker 內嵌能力」成長為「可信、可治理、可水平擴展的自主服務」。
