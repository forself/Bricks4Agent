# 測試模組分析 (03/29)

日期：2026-03-29
類型：模組程式碼分析

## 模組概述

目前實際使用的驗證入口分成四層：

1. **xUnit unit tests** — 位於 `packages/csharp/tests/unit/`，目前 74 個 C# 單元測試
2. **xUnit integration tests** — 位於 `packages/csharp/tests/integration/`，目前 10 個 broker 整合測試
3. **broker/verify** — 位於 `packages/csharp/broker/verify/`，負責 broker 目前最密集的自驗證流程，包含 artifact delivery、signed download、JSON redaction、middleware bypass 與 `/proj` review flow 行為
4. **e2e-bridge / browser tests** — `packages/csharp/tests/e2e-bridge/` 保留互動式端對端工具，`packages/javascript/browser` 目前已有 91 個 browser tests

目前測試體系已不再只有 console-first 驗證。`broker-tests` / `broker/verify` 仍保留，但主要自動化層已包含：

- xUnit（C# unit / integration）
- Vitest（browser tests）
- Playwright smoke

目前可重現的結果快照：

- C# unit：`74/74`
- C# integration：`10/10`
- browser tests：`91/91`

## 測試專案結構

```
packages/csharp/tests/
├── broker-tests/
│   ├── Broker.Tests.csproj          # 參照 Broker.csproj（間接取得 Broker/BrokerCore）
│   ├── Program.cs                   # 主進入點：單元測試 + 整合測試分流
│   ├── IntegrationTest.cs           # HTTP 整合測試（靜態類別）
│   ├── QueryTests.cs                # 查詢品質單元測試（靜態類別）
│   └── BrowserAndDeployTests.cs     # 瀏覽器與部署單元測試（靜態類別）
└── e2e-bridge/
    ├── E2eBridge.csproj             # 參照 BrokerCore.csproj（使用加密模組）
    └── Program.cs                   # 互動式 E2E 測試控制台

packages/csharp/broker/
└── verify/
    ├── Broker.Verify.csproj         # 參照 Broker.csproj
    └── Program.cs                   # broker 自驗證入口
```

### 專案相依性

- `Broker.Tests.csproj` → `Broker.csproj`（透過 `InternalsVisibleTo` 可存取 `internal` 成員）
- `Broker.Verify.csproj` → `Broker.csproj`（直接驗證 broker 服務、端點與序列化行為）
- `E2eBridge.csproj` → `BrokerCore.csproj`（使用 `BrokerCore.Crypto` 進行 ECDH 加密）
- **這些驗證入口均未加入 `ControlPlane.slnx` 主方案檔**，需個別建置與執行。

### 執行方式

```bash
# C# unit
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj -v minimal

# C# integration
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj -v minimal

# broker 自驗證
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj

# 歷史 broker-tests
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj

# 歷史 broker-tests 整合模式（需先啟動 Broker）
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj -- --integration http://localhost:{port}

# browser tests
npm.cmd --prefix packages/javascript/browser run test

# E2E Bridge（需先啟動 Broker + 設定 BROKER_PUB_KEY 環境變數）
dotnet run --project packages/csharp/tests/e2e-bridge/E2eBridge.csproj -- --broker http://localhost:5000
```

## 單元與自驗證測試

### 測試分層

目前實際上有兩條不同性質的程式內驗證路徑：

1. **`broker-tests`**：保留傳統的查詢、HTML 擷取、部署結果模型與 local-admin API 整合測試
2. **`broker/verify`**：集中驗證 broker 近期待碼變動最多、最容易回歸的區段

`broker/verify/Program.cs` 現在已經取代舊版「只測輸出字串 helper」的角色，直接建構 in-memory / sandbox 資源並驗證完整行為，重點包括：

- `BrokerArtifactDownloadOptions` 預設值與 `SidecarPublicUrlResolver` 路徑解析
- `BrokerArtifactDownloadService` 的 signed URL 產生、簽名驗證、過期處理與路徑硬化
- `ArtifactDownloadEndpoints.HandleDownloadRequest()` 的 `200/403/410/404` 行為
- broker auth / encryption middleware 對 `GET /api/v1/artifacts/download/{artifactId}` 的 bypass
- `LineArtifactDeliveryService` 在以下情境的通知內容與狀態：
  - Google Drive 上傳成功
  - Google Drive 失敗且 broker public URL 可用，改送 broker 簽名下載連結
  - Google Drive 失敗且無法產生 public URL，退回無連結降級訊息
  - local-only 交付，不應誤報 Drive failure，也不應出現 broker fallback link
- JSON serialization redaction：
  - artifact `DocumentsRoot`
  - artifact `FilePath`
  - delivery `Artifact`
  - Google Drive `SourcePath`
- 檔案安全邊界：
  - out-of-root 路徑
  - `..` 型檔名
  - hard link
  - junction
  - 缺檔

### `broker-tests` 保留範圍

#### 1. 查詢品質測試（`QueryTests.cs`）

測試 `HighLevelRelationQueryService` 和 `HighLevelQueryToolMediator` 的靜態方法：

**`IsReasonableAdministrativeTerm()`** — 驗證行政區名稱過濾邏輯（12 個斷言）：

| 類別 | 測試案例 | 預期 |
|------|----------|------|
| 接受：標準行政區 | 「台北市」、「信義區」、「臺中市」 | `true` |
| 接受：帶後綴的長名稱 | 「臺北市信義區」（6 字元）、「新北市板橋區」（6 字元） | `true` |
| 接受：英文名稱 | "Taipei City"、"Xinyi District" | `true` |
| 拒絕：過長 CJK 無後綴 | 「這是一個超級長的名稱但沒有行政區後綴」 | `false` |
| 拒絕：含助詞/標點 | 「台北的」、「台北，台中」 | `false` |
| 拒絕：空值 | 空字串、`null` | `false` |

**`BuildTransportReply()`（空結果）** — 驗證鐵路查詢無結果時的回覆格式（3 個斷言）：
- 包含「沒有取得可用班次結果」
- 包含使用範例 `?rail`
- 包含格式 `台北 台中`

**`BuildSearchReply()`（空結果）** — 驗證搜尋無結果時的回覆格式（3 個斷言）：
- 包含「沒有取得可用結果」
- 包含建議「具體的關鍵詞」
- 包含建議「英文搜尋」

#### 2. 瀏覽器與部署測試（`BrowserAndDeployTests.cs`）

測試 `BrowserExecutionHtmlExtractor` 和 `AzureIisDeploymentHealthCheckResult`：

- `ExtractTitle()`：標題擷取與 HTML entity decode
- `ExtractDescription()`：`meta description` 擷取
- `ExtractText()`：主內容抽取並排除 `nav/footer`
- `AzureIisDeploymentHealthCheckResult.Skipped()`：部署健康檢查結果模型

### 測試模式

1. **Console-first 驗證**：所有測試與 verify 都是可直接 `dotnet run` 的 console 入口
2. **靜態方法 + 真服務混合**：`broker-tests` 偏向靜態 helper / API flow，`broker/verify` 會實際建構 DB、服務與 endpoint handler
3. **通過計數器追蹤**：各測試檔案維護自己的 `passed`/`failed` 計數器
4. **字串與行為並重**：不只驗證回覆內容，也驗證 status code、JSON redaction、檔案串流與安全邊界
5. **非中斷式執行**：斷言失敗不拋例外，所有測試繼續執行直到結束

### 關鍵測試類別

| 類別 | 檔案 | 性質 | 測試對象 |
|------|------|------|----------|
| `Broker.Verify` | `packages/csharp/broker/verify/Program.cs` | broker 自驗證 | `BrokerArtifactDownloadService`, `ArtifactDownloadEndpoints`, `LineArtifactDeliveryService`, serialization redaction, middleware bypass |
| `QueryTests` | `QueryTests.cs` | 單元測試 | `HighLevelRelationQueryService.IsReasonableAdministrativeTerm`, `HighLevelQueryToolMediator.BuildTransportReply`, `BuildSearchReply` |
| `BrowserAndDeployTests` | `BrowserAndDeployTests.cs` | 單元測試 | `BrowserExecutionHtmlExtractor.ExtractTitle/Description/Text`, `AzureIisDeploymentHealthCheckResult` |
| `IntegrationTest` | `IntegrationTest.cs` | HTTP 整合測試 | local-admin login、artifact deliver/list/retry、admin HTML |

## 整合測試

### 測試範圍

整合測試透過 HTTP 呼叫一個執行中的 Broker 實例，測試完整的 API 端點流程。位於 `IntegrationTest.cs`，類別名稱 `Broker.Tests.IntegrationTest`。

### 測試設定與啟動

- 整合測試**不自動啟動 Broker**，需要手動先啟動
- 透過 `--integration [broker-url]` 命令列參數觸發（預設 `http://localhost:5000`）
- 使用靜態 `HttpClient`（`Timeout = 30s`）
- 支援 cookie 為基礎的認證（從 `Set-Cookie` header 擷取 session cookie）
- 若無法登入（`_adminToken` 為空），跳過所有整合測試並回報 `(0, 1)`

### Broker 整合測試流程

整合測試為**線性流程**，每一步依賴前一步的結果：

```
Step 1: LoginAdmin()
  ↓ (取得認證 cookie)
Step 2: TestDeliverArtifact()
  ↓ (取得 deliveryResult JSON，含 artifactId 和 notificationId)
Step 3: TestListAllArtifacts()
Step 4: TestListUserArtifacts()
Step 5: TestRetryDrive(deliveryResult)
  ↓ (使用 Step 2 的 artifactId)
Step 6: TestRetryNotification(deliveryResult)
  ↓ (使用 Step 2 的 notificationId)
Step 7: TestAdminHtmlDeliveryTab()
```

### 關鍵測試案例

#### LoginAdmin（步驟 1）

- 呼叫 `GET /api/v1/local-admin/status` 檢查密碼狀態（`hasPassword`、`initialPasswordActive`）
- 若為首次登入或 `initialPasswordActive`：以 `admin` → `test1234` 設定密碼
- 若已有密碼：以 `test1234` 登入
- 從回應 header 擷取 cookie 並設定到 `HttpClient.DefaultRequestHeaders`
- 失敗處理：登入失敗時設定 `_failed++`，後續測試將被跳過

#### TestDeliverArtifact（步驟 2）

- `POST /api/v1/local-admin/line/users/artifacts/deliver`
- 建立測試 artifact：
  - `user_id`: `"test_integration_user"`
  - `file_name`: `"integration-test.md"`
  - `format`: `"md"`
  - `content`: `"# Integration Test\n\nThis is a test artifact."`
  - `upload_to_google_drive`: `false`
  - `send_line_notification`: `true`
- 驗證回應欄位（6 個斷言）：
  - `success == true`
  - `overallStatus == "completed"`
  - `fileName == "integration-test.md"`
  - `notification` 和 `artifact` 物件存在
  - `artifact.overallStatus == "completed"`
- 若使用者 profile 不存在，會優雅地跳過（非失敗，印出 `[INFO]`）

#### TestListAllArtifacts（步驟 3）

- `GET /api/v1/local-admin/line/artifacts?limit=10`
- 驗證 200 狀態碼、回應包含 `total` 和 `items` 陣列（3 個斷言）

#### TestListUserArtifacts（步驟 4）

- `GET /api/v1/local-admin/line/users/test_integration_user/artifacts?limit=10`
- 僅驗證 200 狀態碼（1 個斷言）

#### TestRetryDrive（步驟 5）

- `POST /api/v1/local-admin/line/artifacts/{artifactId}/retry-drive`
- 預期回應 200 或 400（因為 artifact 已 completed，非 partial）
- 驗證正確拒絕訊息包含 `"partial"`（2 個斷言）
- 若無 `artifactId` 則跳過

#### TestRetryNotification（步驟 6）

- 先模擬通知失敗：`POST /api/v1/high-level/line/notifications/complete`
  - `notification_id`, `status: "failed"`, `error: "integration_test_simulated_failure"`
- 再重試通知：`POST /api/v1/local-admin/line/notifications/{notificationId}/retry`
- 驗證 200 狀態碼和回應包含 `"pending"`（2 個斷言）
- 若無 `notificationId` 則跳過

#### TestAdminHtmlDeliveryTab（步驟 7）

- `GET /line-admin.html`
- 驗證 HTML 包含 delivery tab 相關 DOM 元素（7 個斷言）：
  - `data-tab="delivery"`
  - `id="tab-delivery"`
  - `id="delivery-list"`
  - `id="delivery-status-filter"`
  - JavaScript 函式 `retryDriveUpload`
  - JavaScript 函式 `loadDeliveryHistory`
  - HTTP 200 狀態碼

## E2E Bridge 測試工具

`e2e-bridge/Program.cs` 是一個**互動式端對端測試控制台**，而非自動化測試。它實作了完整的 Broker 通訊協定：

### 功能

1. **ECDH 交握**：使用 `ECDiffieHellman` (P-256/nistP256) 生成客戶端密鑰對，與 Broker 公鑰進行 Diffie-Hellman 密鑰交換。HKDF 推導分兩階段：
   - 交握密鑰：`HKDF(shared_secret, salt=nonce, info="broker-handshake-v1")`
   - Session 密鑰：`HKDF(shared_secret, salt=session_id, info="broker-session-v1")`
2. **Session 建立**：透過 `POST /api/v1/sessions/register` 註冊 session，取得 `scoped_token`
3. **加密通訊**：所有後續請求以 AES-256-GCM 加密/解密，AAD 格式為 `req:{session_id}{seq}{path}`（回應為 `resp:...`）
4. **互動式指令迴圈**：以 2 秒間隔輪詢 LINE 訊息並處理指令

### 支援的指令

| 指令 | Broker 能力 | 說明 |
|------|-------------|------|
| `/list [path]` | `file.list` | 列出目錄內容（預設 `/workspace`，depth=2） |
| `/read <file>` | `file.read` | 讀取檔案（limit=50 行） |
| `/search <pattern>` | `file.search` | 搜尋內容（max_results=10） |
| `/notify <msg>` | `line.notification.send` | 發送 LINE 通知 |
| `/approve <desc>` | `line.approval.request` | 發起審批流程（timeout=60s） |
| `/workers` | 直接 `GET /api/v1/workers` | 列出已註冊的 workers |
| 數學運算 | 本地處理 | 基本四則運算（`1+2`、`3*4`） |
| 其他文字 | Echo 回應 | 回傳原文 + 指令說明 |

### 內含元件

- **`BrokerApiClient`** 類別（~290 行）：封裝完整的 Broker 通訊協定
  - `GetAsync(path, ct)` — 未加密的 GET 請求
  - `RegisterSessionAsync(principalId, taskId, roleId, brokerUrl, ct)` — ECDH 交握 + session 註冊
  - `ExecuteAsync(capabilityId, route, payloadJson, ct)` — 加密的執行請求
  - `GetBrokerPubKeyAsync(ct)` — 取得 Broker 公鑰（環境變數或 health 端點）
  - `EncryptRequest(plaintext, seq, path)` — AES-256-GCM 加密
- **`LineMessage`** 資料模型：`Text`、`Type`、`UserId`、`Timestamp`
- **`TryParseMessages(json)`**：解析三層 Broker 回應格式（`data.result_payload` / `result_payload` / 直接 JSON）
- **`ProcessCommand(client, input, ct)`**：指令路由與分發
- **`IsSimpleMath(input, out result)`**：基本四則運算解析
- **`FormatResult(tool, raw)`**：Broker 回應格式化（截斷超過 4500 字元）

### 注意事項

- 需要 `BROKER_PUB_KEY` 環境變數，或 Broker 在 `/api/v1/health` 端點公開 `broker_public_key` / `brokerPublicKey`
- 使用 2 秒輪詢間隔，每 30 個循環印出心跳訊息
- 非自動化測試，需手動操作和觀察
- 序列號（`_seq`）使用 `Interlocked.Increment` 確保執行緒安全
- 冪等鍵格式：`{sessionId}-{seq}-{unixTimestampMs}`

## 測試輔助工具

### 自製斷言函式

每個測試檔案均定義自己的斷言輔助方法（**無共用基底類別**，斷言函式在各檔案重複定義）：

| 函式 | 出現位置 | 行為 |
|------|----------|------|
| `AssertContains(name, actual, expected)` | Program.cs, QueryTests, BrowserAndDeployTests | 驗證字串包含；失敗時印出前 200 字元的 actual |
| `AssertNotContains(name, actual, notExpected)` | Program.cs, BrowserAndDeployTests | 驗證字串不包含 |
| `AssertTrue(name, condition)` | IntegrationTest, QueryTests, BrowserAndDeployTests | 驗證布林條件為 true |
| `AssertFalse(name, condition)` | QueryTests | 驗證布林為 false |
| `AssertEqual(name, actual, expected)` | IntegrationTest, BrowserAndDeployTests | 驗證字串相等 |

所有斷言函式的模式一致：
- 通過時印出 `[PASS] {name}` 到 stdout 並遞增 `passed` 計數器
- 失敗時印出 `[FAIL] {name}: ...` 到 stderr 並遞增 `failed` 計數器
- **不拋出例外**，所有測試會繼續執行

### 計數器與結果回報

- `Program.cs` 中的斷言使用 top-level 變數 `passed`/`failed`（closure capture）
- `QueryTests`、`BrowserAndDeployTests`、`IntegrationTest` 使用靜態欄位 `_passed`/`_failed`
- 各模組返回 `(int passed, int failed)` tuple，`Program.cs` 彙總後以 `Environment.Exit(1)` 回報失敗

### InternalsVisibleTo

`Broker.csproj` 目前只設定 `<InternalsVisibleTo Include="Broker.Tests" />`。也就是說：

- `packages/csharp/tests/broker-tests/` 可以直接存取部分 `internal` 成員
- `packages/csharp/broker/verify/` 不依賴額外 `InternalsVisibleTo`，而是以公開 service、endpoint handler 與序列化結果做驗證

## 測試覆蓋率分析

### 已覆蓋的區域

| 服務 | 檔案位置 | 覆蓋程度 | 說明 |
|------|----------|----------|------|
| `BrokerArtifactDownloadService` | `broker/Services/BrokerArtifactDownloadService.cs` | 高 | signed URL、TTL、簽名驗證、路徑硬化 |
| `ArtifactDownloadEndpoints` | `broker/Endpoints/ArtifactDownloadEndpoints.cs` | 高 | `200/403/410/404`、attachment disposition、generic content type |
| `LineArtifactDeliveryService` | `broker/Services/LineArtifactDeliveryService.cs` | 高 | Drive 成功、Drive 失敗 fallback、degraded fallback、local-only neutral behavior |
| Artifact / Drive serialization redaction | `broker/Services/*`, `broker-core/Contracts/*` | 高 | `DocumentsRoot`、`FilePath`、`Artifact`、`SourcePath` 不對外序列化 |
| `HighLevelRelationQueryService.IsReasonableAdministrativeTerm` | `broker/Services/HighLevelRelationQueryService.cs` | 中高 | 正面和負面案例，含邊界條件 |
| `HighLevelQueryToolMediator.BuildTransportReply` | `broker/Services/HighLevelQueryToolMediator.cs` | 低 | 僅測試空結果場景 |
| `HighLevelQueryToolMediator.BuildSearchReply` | `broker/Services/HighLevelQueryToolMediator.cs` | 低 | 僅測試空結果場景 |
| `BrowserExecutionHtmlExtractor` | `broker/Services/BrowserExecutionHtmlExtractor.cs` | 中 | Title、Description、Text 擷取 |
| `AzureIisDeploymentHealthCheckResult` | `broker/Services/AzureIisDeploymentHealthCheckService.cs` | 低 | `Skipped()` 工廠和欄位存在性 |
| Admin API 端點（整合測試） | 多個 API 端點 | 中 | local-admin login / delivery / retry / delivery tab happy path |
| ECDH 加密通訊（e2e-bridge） | 端對端手動 | 手動 | 完整通訊協定但需人工驗證 |

### 未覆蓋的區域（明顯缺口）

以下區域仍然是明顯缺口；雖然 `broker/verify` 已補上 artifact delivery 與 signed download，但高階協調主流程仍缺乏成套自動化覆蓋：

| 服務 | 檔案 | 重要性 | 說明 |
|------|------|--------|------|
| `HighLevelCoordinator`（主流程） | `HighLevelCoordinator.cs` | 極高 | 核心協調器的 `HandleHighLevelAsync` 等主要入口方法未測試 |
| `HighLevelCommandParser` | `HighLevelCommandParser.cs` | 高 | 使用者指令解析邏輯 |
| `HighLevelWorkflowStateMachine` | `HighLevelWorkflowStateMachine.cs` | 高 | 工作流程狀態機轉換邏輯 |
| `HighLevelInputTrustPolicy` | `HighLevelInputTrustPolicy.cs` | 高 | 輸入信任與過濾策略 |
| `HighLevelExecutionPromotionGate` | `HighLevelExecutionPromotionGate.cs` | 高 | 執行核准閘道（Governance 核心） |
| `HighLevelExecutionModelPlanner` | `HighLevelExecutionModelPlanner.cs` | 中高 | LLM 規劃邏輯 |
| `HighLevelDocumentArtifactService` | `HighLevelDocumentArtifactService.cs` | 中 | 文件生成服務（僅測試 Result 類別的格式化） |
| `HighLevelCodeArtifactService` | `HighLevelCodeArtifactService.cs` | 中 | 程式碼 artifact 生成 |
| `HighLevelSystemScaffoldService` | `HighLevelSystemScaffoldService.cs` | 中 | 系統腳手架服務 |
| `HighLevelLineWorkspaceService` | `HighLevelLineWorkspaceService.cs` | 中 | LINE 工作區管理 |
| `HighLevelMemoryStore` | `HighLevelMemoryStore.cs` | 中 | 記憶存儲 |
| `HighLevelInteractionRecorder` | `HighLevelInteractionRecorder.cs` | 中 | 互動記錄 |
| `HighLevelInterpretationStore` | `HighLevelInterpretationStore.cs` | 中 | 解釋存儲 |
| `HighLevelExecutionIntentStore` | `HighLevelExecutionIntentStore.cs` | 中 | 執行意圖存儲 |
| `HighLevelWorkflowAdminService` | `HighLevelWorkflowAdminService.cs` | 中 | 工作流程管理 |
| `LineChatGateway` | `LineChatGateway.cs` | 中 | LINE 訊息閘道 |
| `LocalAdminAuthService` | `LocalAdminAuthService.cs` | 中 | Admin 認證（整合測試間接測試了登入） |
| `GoogleDriveOAuthService` | `GoogleDriveOAuthService.cs` | 中 | OAuth 流程 |
| `GoogleDriveShareService` | `GoogleDriveShareService.cs` | 中 | Drive 分享邏輯 |
| `BrowserBindingService` | `BrowserBindingService.cs` | 中 | 瀏覽器綁定管理 |
| `BrowserExecutionRuntimeService` | `BrowserExecutionRuntimeService.cs` | 中 | 瀏覽器執行引擎 |
| `BrowserExecutionPreviewService` | `BrowserExecutionPreviewService.cs` | 中 | 瀏覽器預覽服務 |
| `BrowserExecutionRequestBuilder` | `BrowserExecutionRequestBuilder.cs` | 中 | 瀏覽器請求建構 |
| `AzureIisDeploymentExecutionService` | `AzureIisDeploymentExecutionService.cs` | 中 | 部署執行服務 |
| `AzureIisDeploymentTargetService` | `AzureIisDeploymentTargetService.cs` | 中 | 部署目標管理 |
| `AzureIisDeploymentRequestBuilder` | `AzureIisDeploymentRequestBuilder.cs` | 中 | 部署請求建構 |
| `AzureIisDeploymentPreviewService` | `AzureIisDeploymentPreviewService.cs` | 中 | 部署預覽 |
| `AzureIisDeploymentSecretResolver` | `AzureIisDeploymentSecretResolver.cs` | 中 | 部署密碼解析 |
| `ToolSpecRegistry` | `ToolSpecRegistry.cs` | 中 | 工具規格註冊 |
| `ToolSpecCapabilitySyncService` | `ToolSpecCapabilitySyncService.cs` | 中 | 工具規格同步 |
| `ToolSpecStatusChecker` | `ToolSpecStatusChecker.cs` | 中 | 工具狀態檢查 |
| `TdxApiService` | `TdxApiService.cs` | 中 | TDX 交通 API |
| `ProcessRunner` | `ProcessRunner.cs` | 低 | 外部程序執行 |
| `HighLevelLlmOptions` | `HighLevelLlmOptions.cs` | 低 | LLM 選項配置 |

**BrokerCore 層完全未測試**（安全與治理核心）：

| 服務 | 檔案 | 重要性 | 說明 |
|------|------|--------|------|
| `PolicyEngine` | `Services/PolicyEngine.cs` | 極高 | 策略引擎（RBAC 檢查） |
| `SessionService` | `Services/SessionService.cs` | 極高 | Session 建立與管理 |
| `ScopedTokenService` | `Services/ScopedTokenService.cs` | 極高 | Token 簽發與驗證（JWT） |
| `EnvelopeCrypto` | `Crypto/EnvelopeCrypto.cs` | 極高 | 加密/解密（e2e-bridge 間接測試） |
| `TaskRouter` | `Services/TaskRouter.cs` | 高 | 任務路由 |
| `BrokerService` | `Services/BrokerService.cs` | 高 | Broker 主要服務入口 |
| `CapabilityCatalog` | `Services/CapabilityCatalog.cs` | 高 | 能力目錄 |
| `RevocationService` | `Services/RevocationService.cs` | 中 | 撤銷機制 |
| `PlanEngine` / `PlanService` | `Services/PlanEngine.cs` / `PlanService.cs` | 中 | 計畫引擎 |
| `SharedContextService` | `Services/SharedContextService.cs` | 中 | 共享上下文 |
| `ObservationService` | `Services/ObservationService.cs` | 中 | 觀察事件服務 |
| `AuditService` | `Services/AuditService.cs` | 中 | 稽核服務 |
| `SchemaValidator` | `Services/SchemaValidator.cs` | 中 | Schema 驗證 |
| `BrokerDb` / `BrokerDbInitializer` | `Data/BrokerDb.cs` / `BrokerDbInitializer.cs` | 中 | 資料庫初始化 |
| `LlmProxyService` | `Services/LlmProxyService.cs` | 中 | LLM 代理服務 |
| `EmbeddingService` / `RagPipelineService` | `Services/EmbeddingService.cs` / `RagPipelineService.cs` | 中 | 向量嵌入與 RAG |
| `AgentSpawnService` | `Services/AgentSpawnService.cs` | 中 | Agent 啟動服務 |
| `DbSessionKeyStore` / `CacheSessionKeyStore` | `Crypto/` | 中 | Session 密鑰存儲 |

## 相依性

### broker-tests

```
Broker.Tests.csproj
  └── Broker.csproj (Microsoft.NET.Sdk.Web)
        ├── BrokerCore.csproj
        │     ├── BaseOrm.csproj (資料庫 ORM)
        │     └── CacheClient.csproj (分散式快取)
        ├── FunctionPool.csproj (功能池)
        ├── RateLimiting.csproj (速率限制)
        └── BaseLogger.csproj (日誌)
```

- 無外部測試框架（無 xUnit、NUnit、MSTest）
- 無 Mock 框架（無 Moq、NSubstitute）
- 無程式碼覆蓋率工具（無 coverlet）
- 無 assertion 函式庫（無 FluentAssertions、Shouldly）
- NuGet 依賴僅來自被測專案本身

### e2e-bridge

```
E2eBridge.csproj
  └── BrokerCore.csproj
        ├── BaseOrm.csproj
        └── CacheClient.csproj
```

- 使用 `System.Security.Cryptography`（`ECDiffieHellman`、`AesGcm`、`HKDF`、`CryptographicOperations`）
- 匯入 `BrokerCore.Crypto` 命名空間（`using BrokerCore.Crypto;`），但加密邏輯實際上在 `BrokerApiClient` 自行實作
- 使用 `System.Text.Json`（`JsonDocument`、`JsonNode`、`JsonSerializer`）進行 JSON 處理

## 目前狀態與成熟度

### 評估

| 維度 | 評分 | 說明 |
|------|------|------|
| 框架成熟度 | 低 | 無使用標準測試框架，自製斷言在各檔案重複定義，無 test discovery/filtering/parallel execution |
| 單元測試覆蓋率 | 低 | 僅覆蓋約 6 個服務中的靜態方法；38+ 個 Broker 服務檔案和 17+ 個 BrokerCore 服務檔案大部分未測試 |
| 安全性測試 | 極低 | ECDH/AES-GCM 加密管線、PolicyEngine、ScopedTokenService 等核心安全元件僅有 e2e-bridge 手動測試 |
| 核心邏輯測試 | 極低 | `PolicyEngine`、`SessionService`、`HighLevelCoordinator.HandleHighLevelAsync` 主流程無任何自動化測試 |
| 整合測試 | 中 | Artifact delivery 的 happy path 有完整覆蓋；Admin API 的 CRUD 和認證流程有基本驗證 |
| CI/CD 整合 | 低 | 測試專案未加入 `ControlPlane.slnx`；需手動執行 `dotnet run` |
| 斷言品質 | 中 | 主要是字串包含/不包含和布林檢查，缺乏結構化 JSON 驗證或物件圖比較 |
| 錯誤路徑測試 | 低 | 整合測試僅處理 happy path 和已知的 graceful degradation；未覆蓋異常輸入、並行存取、逾時場景 |

### 主要問題

1. **無測試框架**：自製斷言函式在每個檔案重複定義（`AssertContains` 出現 3 次、`AssertTrue` 出現 3 次等），無法使用 test runner 的平行執行、過濾、報告、IDE 整合等功能
2. **無 Mock/Stub**：僅能測試純靜態方法，無法隔離測試有依賴注入的服務。所有有外部依賴的服務（資料庫、HTTP、LLM API）均無法在單元層級測試
3. **覆蓋率嚴重不足**：核心安全元件（策略引擎、session 管理、加密）和治理元件（PromotionGate、InputTrustPolicy）完全缺乏單元測試。估計覆蓋率 < 5%
4. **整合測試需手動 Broker**：沒有使用 `TestServer` 或 `WebApplicationFactory` 進行 in-process 整合測試，導致每次執行整合測試都需要手動啟動 Broker 實例
5. **測試專案未加入 solution**：`ControlPlane.slnx` 不包含測試專案，`dotnet build` 不會自動編譯測試程式碼，可能導致測試程式碼與被測程式碼不同步
6. **E2E Bridge 非自動化**：`e2e-bridge` 需要人工介入操作和觀察結果，無法作為 CI 的一部分
7. **無負面測試**：缺乏對無效輸入、權限不足、並行競爭、大資料量等邊界情況的測試
8. **測試資料清理**：整合測試建立的 `test_integration_user` 相關資料未在測試結束後清理

### 改進建議優先順序

1. 引入 xUnit + `WebApplicationFactory<Program>` 進行 in-process 整合測試，消除手動啟動 Broker 的需求
2. 為 `PolicyEngine`、`SessionService`、`ScopedTokenService`、`EnvelopeCrypto` 加入單元測試（安全核心）
3. 為 `HighLevelCoordinator.HandleHighLevelAsync` 主流程加入測試（業務核心）
4. 將測試專案加入 `ControlPlane.slnx`，確保 CI 建置自動包含測試
5. 引入 Mock 框架（如 NSubstitute）以隔離測試有外部依賴的服務
6. 提取共用的斷言函式到共用基底類別或工具類別
7. 加入程式碼覆蓋率報告（coverlet + ReportGenerator）
8. 為 `HighLevelCommandParser` 加入全面的指令解析測試（高 ROI，純邏輯）
