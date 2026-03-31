# Broker 模組分析 (03/29)

日期：2026-03-29
類型：模組程式碼分析

---

## 1. 模組概述

Broker 是 Bricks4Agent 的 **ASP.NET Core 8 Minimal API 主機**，扮演整個平台的 **控制平面（Control Plane）** 角色。它不自主執行任何業務邏輯，而是作為 Policy Enforcement Point（PEP），接收來自 Agent、LINE Worker 及管理介面的請求，經過加密解密、身份驗證、策略裁決後，將已批准的請求分派至執行層。

核心設計原則：

- **全 POST、E2E 加密**：除健康檢查外，所有 API 走 ECDH + AES-256-GCM 信封加密
- **Broker 不是自主規劃者**：高階模型提議，Broker 驗證並記錄
- **宣告式執行**：執行層消費結構化意圖（`ApprovedRequest`），非原始對話

專案描述（csproj）：`中介核心 — ASP.NET Core 8 Minimal API Host（全 POST、E2E 加密）`

---

## 2. 專案結構

```
packages/csharp/broker/
├── Program.cs                          # 進入點（~995 行）
├── Broker.csproj                       # 專案檔
├── Endpoints/                          # Minimal API 端點定義（18 個檔案）
│   ├── TaskEndpoints.cs
│   ├── SessionEndpoints.cs
│   ├── ExecutionEndpoints.cs
│   ├── CapabilityEndpoints.cs
│   ├── AdminEndpoints.cs
│   ├── AuditEndpoints.cs
│   ├── ContextEndpoints.cs
│   ├── PlanEndpoints.cs
│   ├── RuntimeEndpoints.cs
│   ├── HighLevelEndpoints.cs
│   ├── WorkerEndpoints.cs
│   ├── ToolSpecEndpoints.cs
│   ├── BrowserBindingEndpoints.cs
│   ├── DeploymentAdminEndpoints.cs
│   ├── GoogleDriveOAuthEndpoints.cs
│   ├── LocalAdminEndpoints.cs
│   ├── AgentEndpoints.cs
│   └── ArtifactDownloadEndpoints.cs
├── Middleware/                          # 中間件管線（4 個）
│   ├── BodySizeLimitMiddleware.cs
│   ├── EncryptionMiddleware.cs
│   ├── BrokerAuthMiddleware.cs
│   └── AuditMiddleware.cs
├── Helpers/                            # 工具類別
│   ├── ApiResponseHelper.cs
│   ├── RequestBodyHelper.cs
│   ├── Fts5Utility.cs
│   ├── PayloadHelper.cs
│   └── WebSearchHelper.cs
├── Services/                           # Broker 層服務（~40+ 個檔案）
│   ├── LineChatGateway.cs
│   ├── HighLevelCoordinator (系列)
│   ├── BrowserBinding / Execution 系列
│   ├── AzureIisDeployment 系列
│   ├── GoogleDrive 系列
│   ├── ProjectInterview 系列
│   ├── BrokerArtifactDownloadService.cs
│   ├── SidecarPublicUrlResolver.cs
│   ├── BrokerArtifactDownloadOptions.cs
│   ├── ProjectInterviewStateMachine.cs
│   ├── ProjectInterviewStateService.cs
│   ├── ProjectInterviewRestatementService.cs
│   ├── ProjectInterviewTemplateCatalogService.cs
│   ├── ProjectInterviewProjectDefinitionCompiler.cs
│   ├── ProjectInterviewWorkflowDesignService.cs
│   ├── ProjectInterviewPdfRenderService.cs
│   ├── ToolSpecRegistry.cs
│   ├── LocalAdminAuthService.cs
│   └── ... 等
├── Handlers/                           # 路由處理器（IRouteHandler 實作）
│   ├── Agent/
│   ├── Browser/
│   ├── ConvLog/
│   ├── Delivery/
│   ├── Deploy/
│   ├── File/
│   ├── Memory/
│   ├── Rag/
│   ├── Travel/
│   └── Web/
├── Adapters/
│   └── InProcessDispatcher.cs          # 預設執行分派器
├── Scripts/                            # RAG 種子腳本
│   ├── SeedConsumerProtectionLaw.cs
│   ├── RagIngestService.cs
│   └── seed-consumer-protection-rag.cs
├── wwwroot/                            # 靜態資源
│   ├── index.html
│   └── line-admin.html
├── tool-specs/                         # 工具規格定義檔
└── verify/                             # 驗證子專案（已排除主編譯）
```

---

## 3. 進入點與啟動流程

進入點為 `Program.cs`（約 995 行），使用 ASP.NET Core 的 top-level statement 風格。啟動流程依序為：

### 3.1 資料庫初始化（Step 1）

```csharp
var dbPath = builder.Configuration.GetValue<string>("Database:Path") ?? "broker.db";
var connectionString = $"Data Source={dbPath}";
builder.Services.AddSingleton(sp => BrokerDb.UseSqlite(connectionString));
```

使用 `BrokerDbInitializer` 建立 **27+ 張表**（含 FTS5 虛擬表）並植入種子資料（角色、能力、開發用 Principal/Task）。支援 `DevelopmentSeed` 和 `DashboardSeed` 兩組種子配置。

### 3.2 加密基礎建設（Step 2）

- **ECDH P-256 金鑰對**：可從 `Broker:Encryption:EcdhPrivateKeyBase64` 配置讀取，否則自動生成（開發模式）。類別為 `EnvelopeCrypto`（實作 `IEnvelopeCrypto`）
- **Session 金鑰存儲**：`DbSessionKeyStore`（主金鑰加密），可選 `CacheSessionKeyStore`（分散式快取裝飾器）
- **分散式快取**：條件式啟用 `CacheClient`，支援多節點連線池、`FallbackDecorator` 降級

### 3.3 Token / Session / Epoch（Step 3）

- `ScopedTokenService`：獨立 JWT 實作（secret、issuer、audience、expiration 可配置）
- `SessionService`：Session 生命週期管理
- `RevocationService` / `CacheRevocationService`：撤權服務（Epoch 機制，DB 或 Cache 後端）

### 3.4 稽核 / 能力 / 策略（Step 4-5）

- `AuditService`：稽核記錄
- `CapabilityCatalog` / `CacheCapabilityCatalog`：能力白名單查詢（支援 Cache 裝飾）
- `PolicyEngine`：確定性策略裁決（7 條規則），配置由 `PolicyEngineOptions` 提供
- `TaskRouter`：任務類型 → 風險等級 / 推薦角色映射
- `SchemaValidator`：JSON Schema 驗證

### 3.5 LLM / RAG / 嵌入服務

- `LlmProxyService`：LLM 代理（支援 Ollama / OpenAI 格式），由 `LlmProxyOptions` 配置
- `EmbeddingService`：Ollama `/api/embed` 向量嵌入，由 `EmbeddingOptions` 配置
- `RagPipelineService`：查詢改寫 + 重排序 + 嵌入快取
- `LineChatGateway`：LINE 使用者對話 ↔ LLM 閘道
- `HighLevelCoordinator`：高階協調器（LINE 訊息處理核心入口），由 `HighLevelCoordinatorOptions` 配置；現已包含 `/proj` project interview path、review version regeneration 與 user-facing bilingual prompts
- `HighLevelQueryToolMediator`：高階查詢工具仲裁器
- `HighLevelRelationQueryService`：關聯查詢服務
- `HighLevelExecutionModelPlanner`：執行模型規劃器

### 3.6 執行分派器（Step 6-7）

根據 `FunctionPool:Enabled` 和 `FunctionPool:StrictMode` 配置決定分派策略：

| 模式 | 類別 | 行為 |
|------|------|------|
| FunctionPool 開啟 + StrictMode | `StrictPoolDispatcher` | 無 Worker 直接 fail，絕不降級 |
| FunctionPool 開啟 + 非 Strict | `FallbackDispatcher` | Pool + InProcess 降級 |
| FunctionPool 關閉 | `InProcessDispatcher` | 純行內處理 |

`InProcessDispatcher` 注入的依賴包括 `IProcessRunner`、`AgentSpawnService`、`BrokerDb`、`EmbeddingService`、`RagPipelineService`、`BrowserExecutionRuntimeService`、`AzureIisDeploymentExecutionService`、`GoogleDriveShareService`、`TdxApiService`。

容器管理器（`IContainerManager`）在啟用時為 `ContainerManager`（Docker/Podman），否則為 `NoOpContainerManager`。

### 3.7 中間件管線（順序關鍵）

```csharp
app.UseStaticFiles();                     // Dashboard UI（在加密之前）
app.UseBodySizeLimit(maxBodyBytes);       // [0] 防 DoS（預設 1MB）
app.UseEnvelopeEncryption();              // [1] ECDH+AES-GCM 信封解密/加密
app.UseBrokerAuth();                      // [2] Scoped Token 驗證 + Epoch 閘道
app.UseBrokerAudit();                     // [3] 稽核記錄
```

注意：
- `UseStaticFiles()` 必須在加密/認證中間件之前，確保靜態資源可直接存取。
- `GET /api/v1/artifacts/download/{artifactId}` 也會刻意繞過 broker auth 與 envelope encryption，改由 query string 的 `exp`/`sig` 簽名驗證授權。

### 3.8 RAG 種子 & FTS5 回補

啟動後自動：
1. 植入消費者保護法 RAG 資料（首次，`SeedConsumerProtectionLaw.SeedAsync()`）
2. 回補缺失的向量嵌入（Embedding model 上線後，逐筆回填）
3. 重建 FTS5 索引（CJK 分詞修正：偵測連續 CJK 字元無空格分隔時重建）

---

## 4. API 端點

所有業務端點均掛載於 `/api/v1` 路由群組下（`var api = app.MapGroup("/api/v1")`）。端點註冊由各 `*Endpoints.Map(api)` 靜態方法完成。

### 4.1 核心控制平面端點

#### Health（健康檢查）
| 方法 | 路徑 | 說明 |
|------|------|------|
| GET/POST | `/api/v1/health` | 系統健康 + broker 公鑰 |

#### TaskEndpoints (`/api/v1/tasks/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/tasks/create` | 建立任務（`IBrokerService.CreateTask()`，含風險評估 + 稽核） |
| POST | `/tasks/query` | 查詢任務（`IBrokerService.GetTask()`） |
| POST | `/tasks/cancel` | 取消任務（`IBrokerService.CancelTask()`，級聯撤權） |

#### SessionEndpoints (`/api/v1/sessions/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/sessions/register` | 註冊 Session（ECDH 交握 + Token 發行 + Grant 計畫 + 金鑰派生） |
| POST | `/sessions/heartbeat` | Session 心跳 |
| POST | `/sessions/close` | 關閉 Session（清除 session key） |

`/sessions/register` 是整個系統的關鍵交握端點，執行完整的 Principal 驗證 → Task 狀態檢查 → 角色解析 → Session 建立 → 金鑰派生 → Grant 計畫生成 → Token 發行流程。

#### ExecutionEndpoints (`/api/v1/execution-requests/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/execution-requests/submit` | 提交執行請求（核心 PEP 16 步流程，`IBrokerService.SubmitExecutionRequestAsync()`） |
| POST | `/execution-requests/query` | 查詢執行結果 |

#### CapabilityEndpoints (`/api/v1/capabilities/` & `/api/v1/grants/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/capabilities/list` | 列出能力白名單（支援篩選） |
| POST | `/grants/list` | 列出當前 principal/task/session 的所有有效授予 |

### 4.2 管理端點

#### AdminEndpoints (`/api/v1/admin/`)
| 方法 | 路徑 | 說明 | 權限 |
|------|------|------|------|
| POST | `/admin/kill-switch` | Epoch 遞增（所有舊 Token 即時失效） | role_admin |
| POST | `/admin/revoke` | 撤權（Session/Grant/Token，三種 `RevocationTargetType`） | role_admin |
| POST | `/admin/principals/create` | 註冊主體（`ActorType`: Human/AI/System） | role_admin |
| POST | `/admin/roles/create` | 定義角色 | role_admin |
| POST | `/admin/epoch/query` | 查詢當前 Epoch | 任何已認證角色 |

#### LocalAdminEndpoints (`/api/v1/local-admin/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/local-admin/status` | 管理系統狀態 |
| POST | `/local-admin/login` | 本地密碼登入（支援首次登入改密） |
| POST | `/local-admin/change-password` | 修改密碼 |
| POST | `/local-admin/logout` | 登出 |

此端點群組走 **plain JSON**（不走加密中間件），由 `IsPlainJsonTrustedPath()` 排除。

#### ArtifactDownloadEndpoints (`/api/v1/artifacts/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/artifacts/download/{artifactId}?exp=...&sig=...` | Public signed download endpoint，驗證 HMAC 簽名與過期時間後直接串流 artifact 檔案 |

此端點群組的設計重點：
- 不要求登入，也不走 Broker auth / E2E encryption；授權完全由 `BrokerArtifactDownloadService` 驗證 `artifactId + exp + sig`
- 成功時回傳 `application/octet-stream`，並以安全檔名設定 `Content-Disposition`
- 失敗時僅回 generic status code：`403`（簽名無效）、`410`（過期）、`404`（artifact 或檔案不可用）
- 對外永遠不暴露本機 `DocumentsRoot`、`FilePath` 或其他內部實體路徑

### 4.3 因果工作流端點

#### ContextEndpoints (`/api/v1/context/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/context/write` | 寫入 SharedContext（版本化 + ACL + content_type 限制） |
| POST | `/context/read` | 按 document_id 讀取最新版本 |
| POST | `/context/read-by-key` | 按 key + taskId 讀取（node output 查詢） |
| POST | `/context/list` | 列出 task 下所有條目 |
| POST | `/context/history` | 版本歷史列表 |

#### PlanEndpoints (`/api/v1/plans/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/plans/create` | 建立計畫（綁定 BrokerTask） |
| POST | `/plans/get` | 查詢計畫 |
| POST | `/plans/add-node` | 新增 DAG 節點（含 capability_id、intent、output_context_key） |
| POST | `/plans/add-edge` | 新增 DAG 邊（DataFlow / ControlFlow / ApprovalGate） |
| POST | `/plans/validate` | DAG 驗證（環偵測 + 拓撲排序） |
| POST | `/plans/submit` | 提交並執行計畫（`IPlanEngine.SubmitAndExecuteAsync()`） |
| POST | `/plans/status` | 完整執行狀態（plan + nodes + edges + checkpoints + summary） |

### 4.4 LLM / Runtime 端點

#### RuntimeEndpoints (`/api/v1/runtime/` & `/api/v1/llm/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/runtime/spec` | 取得 Agent 運行時規格（provider、model、capability_ids、llm_routes） |
| POST | `/llm/health` | LLM 可用性檢查 |
| POST | `/llm/models` | 模型列表 |
| POST | `/llm/chat` | LLM 對話（轉發至 Ollama / OpenAI） |

### 4.5 高階協調器端點

#### HighLevelEndpoints (`/api/v1/high-level/line/`)

此端點群組走 **plain JSON**（不走加密中間件和認證中間件），用於 LINE Worker 和管理介面直接呼叫。

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/high-level/line/process` | LINE 訊息處理核心入口（`HighLevelCoordinator.ProcessLineMessageAsync()`） |
| POST | `/high-level/line/profile` | 查詢使用者檔案 |
| POST | `/high-level/line/draft` | 查詢草稿 |
| GET | `/high-level/line/users` | 列出所有 LINE 使用者 |
| POST | `/high-level/line/users/permissions` | 設定使用者權限（query/transport/production/browser/deployment） |
| GET | `/high-level/line/registration-policy` | 查詢匿名註冊策略 |
| POST | `/high-level/line/registration-policy` | 設定註冊策略 |
| POST | `/high-level/line/users/registration/review` | 審核使用者註冊 |
| GET | `/high-level/line/notifications/pending` | 待處理通知列表 |
| POST | `/high-level/line/notifications/complete` | 完成通知 |

### 4.6 瀏覽器代理管理端點

#### BrowserBindingEndpoints (`/api/v1/browser-admin/`)

全部需要 `role_admin` 授權。管理瀏覽器站點綁定（`BrowserSiteBinding`）、使用者授權（`BrowserUserGrant`）、系統綁定（`BrowserSystemBinding`）、Session 租約（`BrowserSessionLease`）。

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/browser-admin/site-bindings/list\|get\|upsert` | 站點綁定 CRUD |
| POST | `/browser-admin/user-grants/list\|get\|upsert` | 使用者授權 CRUD |
| POST | `/browser-admin/system-bindings/list\|get\|upsert` | 系統綁定 CRUD |
| POST | `/browser-admin/leases/list\|get\|issue\|revoke` | 租約管理 |
| POST | `/browser-admin/requests/build` | 建構瀏覽器執行請求（`IBrowserExecutionRequestBuilder.TryBuild()`） |
| POST | `/browser-admin/requests/preview-fetch` | 預覽匿名抓取（`BrowserExecutionPreviewService.ExecuteAnonymousReadAsync()`） |

### 4.7 部署管理端點

#### DeploymentAdminEndpoints (`/api/v1/deployment-admin/`)

全部需要 `role_admin` 授權。管理 Azure VM IIS 部署目標。

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/deployment-admin/targets/list\|get\|upsert` | 部署目標 CRUD |
| POST | `/deployment-admin/requests/build` | 建構部署請求（`IAzureIisDeploymentRequestBuilder.TryBuild()`） |
| POST | `/deployment-admin/requests/preview` | 部署預覽 |
| POST | `/deployment-admin/requests/execute` | 執行部署（支援 dry_run） |

### 4.8 其他端點

#### ToolSpecEndpoints (`/api/v1/tool-specs/`)
走 plain JSON，用於查詢工具規格定義。

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/tool-specs/list` | 工具規格列表（支援篩選） |
| POST | `/tool-specs/get` | 取得特定工具規格 |

#### AgentEndpoints (`/api/v1/agents/`)

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/agents/capabilities` | 可用能力清單（含風險等級、分類） |
| POST | `/agents/capabilities/defaults` | 預設低風險能力集合 |
| POST | `/agents/create` | 建立 Agent（Principal + Task + Grants，最低權限策略） |
| POST | `/agents/spawn` | 生成 Agent 容器（Docker/Podman） |
| POST | `/agents/list` | 列出 Agent |
| POST | `/agents/stop` | 停止 Agent（停用 DB 紀錄 + 停止容器） |
| POST | `/agents/rag/seed-consumer-law` | 消費者保護法 RAG 種子 |
| POST | `/agents/rag/import-json` | RAG JSON 匯入 |
| POST | `/agents/rag/import-csv` | RAG CSV 匯入 |
| POST | `/agents/rag/import-web` | RAG 網路搜尋匯入 |
| POST | `/agents/rag/test` | RAG 測試查詢（BM25 + 向量 + RRF） |

#### GoogleDriveOAuthEndpoints (`/api/v1/google-drive/oauth/`)
| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/google-drive/oauth/callback` | OAuth 回呼（僅 loopback IP 可存取） |

#### WorkerEndpoints (`/api/v1/workers/` — 條件式啟用)

僅在 `FunctionPool:Enabled=true` 時註冊。

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/workers/` | 列出已註冊 Worker（含狀態、能力、負載） |
| POST | `/workers/spawn` | 生成 Worker 容器 |
| POST | `/workers/stop` | 停止 Worker 容器 |
| GET | `/workers/containers` | 列出受管容器 |
| POST | `/workers/logs` | 取得容器日誌 |
| GET | `/workers/health` | 池健康摘要 |

### 4.9 開發端點 (`/dev/`)

不走加密/認證中間件，由 `EncryptionMiddleware` 和 `BrokerAuthMiddleware` 的排除邏輯（`path.StartsWith("/dev/")`）放行。

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/dev/line-chat` | LINE 對話測試 |
| GET | `/dev/conversations` | 對話列表 |
| GET | `/dev/conversations/{userId}` | 使用者對話歷史 |
| DELETE | `/dev/conversations/{userId}` | 清除對話 |
| GET | `/dev/system/status` | 系統狀態（LLM/Embedding/RAG/DB 統計） |
| POST | `/dev/rag-test` | RAG 查詢測試 |
| POST | `/dev/rag-import-json` | RAG JSON 匯入 |
| POST | `/dev/rag-import-csv` | RAG CSV 匯入 |
| POST | `/dev/rag-import-web` | RAG 網路匯入 |

---

## 5. 中間件詳解

### 5.1 BodySizeLimitMiddleware

- 檔案：`Middleware/BodySizeLimitMiddleware.cs`
- 管線位置：最外層
- 預設限制：1 MB（`Broker:MaxRequestBodyBytes`）
- 機制：先檢查 `Content-Length` header（快速拒絕），再設定 Kestrel `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize`
- 擴展方法：`app.UseBodySizeLimit(maxBodyBytes)`

### 5.2 EncryptionMiddleware

- 檔案：`Middleware/EncryptionMiddleware.cs`
- 管線位置：加密層
- 雙模式：
  - **初始交握**：`client_ephemeral_pub` + 無 `session_id` → `IEnvelopeCrypto.DecryptHandshake()` → 解密
  - **已建立 Session**：`session_id` + `seq` → `ISessionKeyStore.Retrieve()` → `TryAdvanceSeq()` replay 防護 → `IEnvelopeCrypto.Decrypt()` AES-GCM 解密
- AAD 格式：`req:{session_id}{seq}{path}`（H-1 修復：方向標記防混淆）
- 回應加密：攔截 response body → 同 session_key 加密回寫（AAD = `resp:{session_id}{seq}{path}`）
- 初始交握回應額外包含明文 `session_id`（供客戶端先讀取再派生金鑰解密）
- 排除路徑：`/api/v1/health`、`/dev/*`、`/api/v1/high-level/line/*`、`/api/v1/tool-specs/*`、`/api/v1/local-admin/*`
- 縱深防禦：額外檢查 encrypted body 不超 2MB（即使上游已有 BodySizeLimitMiddleware）
- 擴展方法：`app.UseEnvelopeEncryption()`

### 5.3 BrokerAuthMiddleware

- 檔案：`Middleware/BrokerAuthMiddleware.cs`
- 管線位置：認證層（在 Encryption 之後）
- 驗證流程：
  1. 從解密 body 的 `scoped_token` 欄位或 `Authorization: Bearer` header 取得 Token
  2. `IScopedTokenService.ValidateToken()` 驗證簽章 + 時效
  3. Epoch 閘道：`token.epoch < current_epoch` → 401
  4. 撤銷檢查：`IRevocationService.IsRevoked(jti)` + `IsRevoked(sessionId)`
  5. 注入 claims 至 `HttpContext.Items`（鍵名：`broker_claims`、`broker_principal_id`、`broker_task_id`、`broker_session_id`、`broker_role_id`、`broker_epoch`）
- 排除路徑：`/api/v1/health`、`/api/v1/sessions/register`、`/api/v1/high-level/line/*`、`/api/v1/tool-specs/*`、`/api/v1/local-admin/*`、`/dev/*`、非 POST 請求
- 擴展方法：`app.UseBrokerAuth()`

### 5.4 AuditMiddleware

- 檔案：`Middleware/AuditMiddleware.cs`
- 管線位置：最內層（在 Auth 之後）
- 每次 API 呼叫記錄兩筆 `AuditEvent`：`API_REQUEST`（含 method、path、content_length）和 `API_RESPONSE`（含 status_code、path）
- trace_id 串聯整個請求生命週期（`Activity.Current?.Id` 或自動生成 GUID）
- 稽核失敗不阻斷業務流程（catch + LogError）
- 擴展方法：`app.UseBrokerAudit()`

---

## 6. 靜態資源

`wwwroot/` 目錄透過 `app.UseStaticFiles()` 提供，位於加密中間件之前（無需加密）：

- `index.html`：Dashboard 首頁
- `line-admin.html`：LINE 管理介面（單一 HTML 檔案，vanilla JS，呼叫 `/api/v1/local-admin/*` 端點，走 plain JSON — 由 `EncryptionMiddleware` 和 `BrokerAuthMiddleware` 明確排除，不經加密/認證中間件）

已硬編碼封鎖（直接回傳 404）的路徑：
- `/dev/admin`
- `/dev/line-users`

---

## 7. 設定與組態

Broker 透過 ASP.NET Core 的 `IConfiguration` 讀取配置（支援 appsettings.json / 環境變數等），主要配置區段：

| 區段 | 說明 |
|------|------|
| `Database:Path` | SQLite 路徑（預設 `broker.db`） |
| `Broker:Encryption:EcdhPrivateKeyBase64` | ECDH 私鑰（production 必設） |
| `Broker:Encryption:MasterKeyBase64` | Session 金鑰主加密金鑰 |
| `Broker:ScopedToken:Secret\|Issuer\|Audience\|ExpirationMinutes` | Token 簽署配置 |
| `Broker:MaxRequestBodyBytes` | 最大請求 body（預設 1,048,576 = 1MB） |
| `CacheCluster:Enabled\|Nodes\|PoolSize\|ConnectTimeoutSeconds\|OperationTimeoutSeconds` | 分散式快取 |
| `PolicyEngine:*` | 策略引擎選項（黑名單、禁止路徑前綴等） |
| `LlmProxy:Enabled\|BaseUrl\|Provider\|DefaultModel\|ApiKey\|ApiFormat\|TimeoutSeconds` | LLM 代理 |
| `HighLevelLlm:BaseUrl\|Provider\|DefaultModel\|ApiKey\|TimeoutSeconds` | 高階 LLM 配置 |
| `HighLevelCoordinator:DraftTtlMinutes\|MaxDraftSummaryLength` | 高階協調器 |
| `HighLevelExecutionModelPolicy:*` | 高階執行模型策略 |
| `LineChatGateway:Enabled\|RagEnabled` | LINE 閘道 |
| `Embedding:Enabled\|Model\|BaseUrl\|Dimension\|TimeoutSeconds` | 嵌入服務 |
| `RagPipeline:QueryRewriteEnabled\|RerankEnabled\|CacheEnabled\|OllamaBaseUrl` | RAG 管線 |
| `FunctionPool:Enabled\|StrictMode\|ListenPort\|BindAddress\|DispatchTimeoutSeconds\|MaxRetries\|MaxWorkers` | 功能池 |
| `FunctionPool:ContainerManager:Enabled\|Runtime\|NetworkName\|WorkerImages` | 容器管理 |
| `ToolSpecRegistry:Root` | 工具規格根路徑 |
| `DeploymentSecrets:*` | 部署密鑰解析 |
| `GoogleDriveDelivery:*` | Google Drive 交付配置 |
| `ArtifactDownload:SigningSecret\|LinkTtlMinutes\|AllowRepeatedDownloads\|SidecarLastTunnelUrlPath` | Broker 簽名下載連結配置 |
| `Tdx:*` | TDX 交通資料配置（API key 等） |
| `DevelopmentSeed:*` | 開發種子資料 |
| `DashboardSeed:*` | Dashboard 種子資料 |

---

## 8. 相依性

### 專案參考

| 專案 | 說明 |
|------|------|
| `broker-core` (`BrokerCore.csproj`) | 核心領域邏輯（純領域層，無 HTTP 依賴） |
| `function-pool` (`FunctionPool.csproj`) | 功能池（Worker 註冊/分派/容器管理） |
| `security/RateLimiting` (`RateLimiting.csproj`) | 限流模組 |
| `logging/BaseLogger` (`BaseLogger.csproj`) | 日誌模組 |

### NuGet 套件

| 套件 | 版本 |
|------|------|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.0 |

### 內部可見性

`InternalsVisibleTo` 開放給 `Broker.Tests`（測試專案）。

---

## 9. Handlers（路由處理器）

Broker 的 `Handlers/` 目錄實作了 `IRouteHandler` 介面（定義於 broker-core），每個處理器負責處理一個特定的 route，由 `InProcessDispatcher` 的 handler registry 查找並呼叫。

| 分類 | 處理器 | 路由 |
|------|--------|------|
| **File** | `ReadFileHandler` | `read_file` |
| | `ListDirectoryHandler` | `list_directory` |
| | `SearchFilesHandler` | `search_files` |
| | `SearchContentHandler` | `search_content` |
| **Memory** | `MemoryStoreHandler` | `memory_store` |
| | `MemoryRetrieveHandler` | `memory_retrieve` |
| | `MemoryDeleteHandler` | `memory_delete` |
| | `MemoryFulltextSearchHandler` | `memory_fulltext_search` |
| | `MemorySemanticSearchHandler` | `memory_semantic_search` |
| **RAG** | `RagRetrieveHandler` | `rag_retrieve` |
| | `RagImportHandler` | `rag_import` |
| | `RagImportWebHandler` | `rag_import_web` |
| **Web** | `WebSearchHandler` | `web_search` |
| | `WebSearchGoogleHandler` | `web_search_google` |
| | `WebSearchDuckDuckGoHandler` | `web_search_duckduckgo` |
| | `WikipediaSearchHandler` | `wikipedia_search` |
| | `WebFetchHandler` | `web_fetch` |
| **ConvLog** | `ConvLogAppendHandler` | `conv_log_append` |
| | `ConvLogReadHandler` | `conv_log_read` |
| **Agent** | `CreateAgentHandler` | `create_agent` |
| | `ListAgentsHandler` | `list_agents` |
| | `StopAgentHandler` | `stop_agent` |
| **Browser** | `BrowserReadHandler` | 瀏覽器讀取操作 |
| **Deploy** | `DeployAzureIisHandler` | Azure IIS 部署 |
| **Delivery** | `DeliveryGoogleDriveShareHandler` | Google Drive 分享 |
| **Travel** | `TravelRailSearchHandler` | 台鐵查詢 |
| | `TravelHsrSearchHandler` | 高鐵查詢 |
| | `TravelBusSearchHandler` | 公車查詢 |
| | `TravelFlightSearchHandler` | 航班查詢 |

---

## 10. 目前狀態與成熟度

### 已完成功能

- **核心 PEP 管線**：完整的 16 步裁決流程，含冪等性（`idempotency_key` + unique index）、Epoch 閘道、配額管理、稽核追蹤
- **E2E 加密**：ECDH P-256 + AES-256-GCM 信封加密，含 replay 防護（序列號遞增）、方向標記 AAD
- **因果工作流**：Plan/Node/Edge DAG 排程引擎，含 DataFlow/ControlFlow/ApprovalGate 邊類型
- **RAG 管線**：BM25（SQLite FTS5）+ 向量語意搜尋（Ollama embed）+ RRF 融合 + LLM 查詢改寫 + 重排序
- **多通道整合**：LINE 高階協調器、瀏覽器代理、Azure IIS 部署、Google Drive 交付、TDX 交通查詢
- **管理介面**：本地認證（`LocalAdminAuthService`）+ Dashboard HTML 管理頁面 + LINE 使用者管理
- **Agent 生命週期**：建立 → 選擇能力 → 生成容器 → 停止，含最低權限策略
- **分散式快取**：條件式 CacheClient 裝飾器（Session key、Revocation、Capability）

### 待完成項目（TODO / Phase 5）

- `ExceptionMiddleware`：全域例外中間件（Program.cs 第 507 行標記 `TODO: Phase 5`）
- `IpRateLimiter`：IP 限流中間件（Program.cs 第 508 行標記 `TODO: Phase 5`）
- Plan DAG 同級並行執行（目前 Phase 4 為循序）
- PEP bypass 消除：`HighLevelQueryToolMediator` 的 `EXC-HLQM-BYPASS`（`PipelineExceptions` 登記，待 Phase 2 消除）

### 程式碼品質標記

程式碼中有系統化的修復標記，反映逐步改善的過程：

- **M 系列（功能修復）**：`M-1`（必填欄位驗證）、`M-2`（PolicyEngine 黑名單配置化）、`M-3`（金鑰不記錄至 log）、`M-5`（StreamReader dispose）、`M-7`（消除冗餘 LoggerFactory）、`M-8`（RequestAborted 取消令牌）、`M-10`（統一回應格式）
- **H 系列（安全/效能修復）**：`H-1`（AAD 方向標記）、`H-3`（消除 sync-over-async）、`H-6`（版本 race condition 修復）、`H-10`（Body 大小限制防 DoS）
- **L 系列（生命週期修復）**：`L-1`（ID 格式統一）、`L-5`（縱深防禦）、`L-7`（健康檢查 GET 支援）、`L-8`（shutdown 超時保護）
